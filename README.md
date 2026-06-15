# Test-Exec

Test-Exec は、指定した実行ファイルや DLL をテスト用に生成し、その実行やファイル書き込み動作を検証するためのコマンドラインツールです。

## 概要

Test-Exec は、検証対象となる実行ファイルを自動生成し、以下の操作を実行できます。

* 署名付きまたは未署名の実行ファイルの作成と実行
* DLL の作成とロード
* BAT ファイルの作成と実行
* 指定したプロセスからのファイル書き込み

実行結果は JSON 形式で出力されます。

---

## コマンド

### run

指定したパスにテスト用ファイルを生成し、実行します。

#### 構文

```powershell
Test-Exec run <type> -Path <path>
```

#### パラメータ

| パラメータ | 説明                                 |
| ----- | ---------------------------------- |
| type  | 生成するファイル種別。`signed` または `unsigned` |
| Path  | 作成および実行するファイルパス                    |

#### 例

```powershell
Test-Exec run unsigned -Path C:\bin\unsigned.exe
```

未署名の実行ファイルを生成して実行します。

```powershell
Test-Exec run signed -Path C:\bin\signed.exe
```

署名付きの実行ファイルを生成して実行します。

#### 対応拡張子

* `.exe`
* `.dll`
* `.bat`

拡張子に応じて Test-Exec が適切なテストファイルを生成します。

---

### write

指定した実行ファイルを生成・起動し、そのプロセスから対象ファイルへ書き込みを行います。

#### 構文

```powershell
Test-Exec write -From <process-path> -Path <target-path>
```

#### パラメータ

| パラメータ | 説明               |
| ----- | ---------------- |
| From  | 書き込みを実行するプロセスのパス |
| Path  | 書き込み対象ファイル       |

#### 例

```powershell
Test-Exec write -From C:\bin\from.exe -Path C:\temp\test.txt
```

Test-Exec は `C:\bin\from.exe` を生成して起動し、そのプロセスから `C:\temp\test.txt` へ書き込みを行います。

---

## 出力

すべてのコマンドは JSON 形式で結果を返します。

### run の出力例

```json
{
  "command": "run",
  "type": "unsigned",
  "path": "C:\\bin\\unsigned.exe",
  "created": true,
  "executed": true,
  "processId": 1234,
  "exitCode": 0,
  "success": true
}
```

### write の出力例

```json
{
  "command": "write",
  "from": "C:\\bin\\from.exe",
  "path": "C:\\temp\\test.txt",
  "processId": 5678,
  "bytesWritten": 128,
  "success": true
}
```

### エラー時の出力例

```json
{
  "success": false,
  "error": {
    "code": "FILE_CREATE_FAILED",
    "message": "Failed to create executable."
  }
}
```

---

## 注意事項

* 指定されたパスにファイルが存在する場合、上書きされる可能性があります。
* 生成されたファイルはテスト用途のみを目的としています。
* 署名付きファイルは Test-Exec が生成するテスト用証明書で署名されます。
* 管理者権限が必要な操作では、実行環境に応じて権限昇格が必要になる場合があります。

