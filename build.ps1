$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# 使用系统 .NET Framework 编译器，不依赖 dotnet CLI 或 NuGet 还原。
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe（.NET Framework 编译器）"
}

# 所有源文件在同一命名空间下，直接编译为无控制台窗口的单个 EXE。
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $root '2.4GBattery.exe'

Write-Host "编译 $($sources.Count) 个源文件 -> $out"

& $csc /nologo /target:winexe /optimize+ /out:"$out" `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败" }

$info = Get-Item $out
Write-Host ("编译成功: {0} ({1:N0} 字节)" -f $info.Name, $info.Length)
