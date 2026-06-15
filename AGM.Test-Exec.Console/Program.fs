open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Text
open System.Text.Json

let jsonOptions = JsonSerializerOptions(WriteIndented = true)

type ExecutionResult =
    { ProcessId: int
      ExitCode: int }

// writeJson: obj -> unit
let writeJson (value: obj) =
    JsonSerializer.Serialize(value, jsonOptions) |> printfn "%s"

// setFailure: string -> string -> unit
let setFailure code message =
    writeJson {| success = false; error = {| code = code; message = message |} |}
    Environment.ExitCode <- 1

// ensureParentDirectory: string -> unit
let ensureParentDirectory (path: string) =
    let fullPath = Path.GetFullPath path
    let parent = Path.GetDirectoryName fullPath
    if not (String.IsNullOrWhiteSpace parent) then
        Directory.CreateDirectory(parent) |> ignore

// currentExecutablePath: unit -> string
let currentExecutablePath () =
    let mainModule = Process.GetCurrentProcess().MainModule
    if isNull mainModule then String.Empty else mainModule.FileName

// currentAssemblyPath: unit -> string
let currentAssemblyPath () = Assembly.GetExecutingAssembly().Location

// makeExecutableIfPossible: string -> unit
let makeExecutableIfPossible path =
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        try
            let chmod = ProcessStartInfo("chmod")
            chmod.ArgumentList.Add("+x")
            chmod.ArgumentList.Add(path)
            chmod.UseShellExecute <- false
            use proc = Process.Start(chmod)
            proc.WaitForExit()
        with _ -> ()

// createBat: string -> unit
let createBat path =
    let content =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            "@echo off" + Environment.NewLine + "echo Test-Exec BAT executed" + Environment.NewLine + "exit /b 0" + Environment.NewLine
        else
            "#!/bin/sh" + Environment.NewLine + "echo Test-Exec BAT executed" + Environment.NewLine + "exit 0" + Environment.NewLine
    File.WriteAllText(path, content, Encoding.UTF8)
    makeExecutableIfPossible path

// createDll: string -> unit
let createDll path =
    let source = currentAssemblyPath()
    if String.IsNullOrWhiteSpace source || not (File.Exists source) then
        invalidOp "Current assembly could not be located."
    File.Copy(source, path, true)

// createExe: string -> bool -> unit
let createExe path signed =
    let source = currentExecutablePath()
    if String.IsNullOrWhiteSpace source || not (File.Exists source) then
        invalidOp "Current executable could not be located."
    File.Copy(source, path, true)
    makeExecutableIfPossible path
    if signed then
        let signatureMarker = Environment.NewLine + "# Test-Exec generated signed test file marker" + Environment.NewLine
        File.AppendAllText(path, signatureMarker, Encoding.UTF8)

// createTestFile: string -> string -> unit
let createTestFile fileType path =
    ensureParentDirectory path
    match Path.GetExtension(path).ToLowerInvariant() with
    | ".bat" -> createBat path
    | ".dll" -> createDll path
    | ".exe" -> createExe path (fileType = "signed")
    | extension when String.IsNullOrWhiteSpace extension -> createExe path (fileType = "signed")
    | extension -> invalidArg "Path" ($"Unsupported extension: {extension}")

// executeFile: string -> ExecutionResult ref -> unit
let executeFile path (result: ExecutionResult ref) =
    let extension = Path.GetExtension(path).ToLowerInvariant()
    let startInfo =
        match extension with
        | ".bat" when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) -> ProcessStartInfo("cmd.exe", "/c \"" + path + "\"")
        | ".bat" -> ProcessStartInfo("/bin/sh", "\"" + path + "\"")
        | ".dll" -> ProcessStartInfo("dotnet", "\"" + path + "\" __probe")
        | _ -> ProcessStartInfo(path, "__probe")
    startInfo.UseShellExecute <- false
    use proc = Process.Start(startInfo)
    proc.WaitForExit()
    result.Value <- { ProcessId = proc.Id; ExitCode = proc.ExitCode }

// findOption: string -> string array -> string option
let findOption name (args: string array) =
    args
    |> Array.tryFindIndex (fun arg -> String.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
    |> Option.bind (fun index -> if index + 1 < args.Length then Some args[index + 1] else None)

// handleRun: string array -> unit
let handleRun (args: string array) =
    if args.Length < 2 then
        setFailure "INVALID_ARGUMENTS" "Usage: Test-Exec run <signed|unsigned> -Path <path>"
    else
        let fileType = args[1].ToLowerInvariant()
        if fileType <> "signed" && fileType <> "unsigned" then
            setFailure "INVALID_TYPE" "type must be 'signed' or 'unsigned'."
        else
            match findOption "-Path" args with
            | None -> setFailure "MISSING_PATH" "-Path is required."
            | Some path ->
                try
                    let fullPath = Path.GetFullPath path
                    createTestFile fileType fullPath
                    let execution = ref { ProcessId = 0; ExitCode = 1 }
                    executeFile fullPath execution
                    writeJson {| command = "run"; ``type`` = fileType; path = fullPath; created = true; executed = true; processId = execution.Value.ProcessId; exitCode = execution.Value.ExitCode; success = (execution.Value.ExitCode = 0) |}
                    Environment.ExitCode <- execution.Value.ExitCode
                with ex -> setFailure "FILE_CREATE_OR_EXECUTE_FAILED" ex.Message

// internalWrite: string -> unit
let internalWrite targetPath =
    try
        let fullPath = Path.GetFullPath targetPath
        ensureParentDirectory fullPath
        let payload = "Test-Exec write payload" + Environment.NewLine
        File.WriteAllText(fullPath, payload, Encoding.UTF8)
        Environment.ExitCode <- 0
    with _ -> Environment.ExitCode <- 1

// handleWrite: string array -> unit
let handleWrite (args: string array) =
    match findOption "-From" args, findOption "-Path" args with
    | Some fromPath, Some targetPath ->
        try
            let fullFrom = Path.GetFullPath fromPath
            let fullTarget = Path.GetFullPath targetPath
            createTestFile "unsigned" fullFrom
            let startInfo = ProcessStartInfo(fullFrom, "__write \"" + fullTarget + "\"")
            startInfo.UseShellExecute <- false
            use proc = Process.Start(startInfo)
            proc.WaitForExit()
            let bytesWritten = if File.Exists fullTarget then FileInfo(fullTarget).Length else 0L
            writeJson {| command = "write"; ``from`` = fullFrom; path = fullTarget; processId = proc.Id; bytesWritten = bytesWritten; success = (proc.ExitCode = 0 && bytesWritten > 0L) |}
            Environment.ExitCode <- proc.ExitCode
        with ex -> setFailure "WRITE_FAILED" ex.Message
    | _ -> setFailure "INVALID_ARGUMENTS" "Usage: Test-Exec write -From <process-path> -Path <target-path>"

[<EntryPoint>]
// main: string array -> unit
let main argv =
    match argv with
    | [| "__probe" |] -> Environment.ExitCode <- 0
    | [| "__write"; targetPath |] -> internalWrite targetPath
    | [||] -> setFailure "INVALID_ARGUMENTS" "Usage: Test-Exec <run|write> ..."
    | args ->
        match args[0].ToLowerInvariant() with
        | "run" -> handleRun args
        | "write" -> handleWrite args
        | command -> setFailure "UNKNOWN_COMMAND" ($"Unknown command: {command}")
