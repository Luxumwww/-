param(
    [string]$OutputDirectory,
    [switch]$SourceOnly
)
$ErrorActionPreference = 'Stop'
$petRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $petRoot 'GitHub发布' }
$petBundle = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $petBundle) { throw 'Output directory already exists. Choose a new -OutputDirectory to preserve existing files.' }
$petSource = Join-Path $petBundle '源码\Hajimi'
$petRelease = Join-Path $petBundle '发布附件'
[void][System.IO.Directory]::CreateDirectory($petSource)
$petUtf8 = [System.Text.UTF8Encoding]::new($false)

function Copy-PetSourceFile([string]$Relative) {
    $petFrom = [System.IO.Path]::GetFullPath((Join-Path $petRoot $Relative))
    if (-not $petFrom.StartsWith($petRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Source file escaped the project directory.' }
    $petTo = [System.IO.Path]::GetFullPath((Join-Path $petSource $Relative))
    if (-not $petTo.StartsWith($petSource + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Destination file escaped the source package.' }
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $petTo))
    Copy-Item -LiteralPath $petFrom -Destination $petTo
}

foreach ($petFile in @('.gitignore','.gitattributes','README.md','CHANGELOG.md','图标.ico','启动哈基米.cmd')) { Copy-PetSourceFile $petFile }
if (Test-Path -LiteralPath (Join-Path $petRoot 'LICENSE')) { Copy-PetSourceFile 'LICENSE' }
foreach ($petTree in @('src','tests')) {
    foreach ($petFile in Get-ChildItem -LiteralPath (Join-Path $petRoot $petTree) -File -Recurse) {
        $petRelative = $petFile.FullName.Substring($petRoot.Length + 1)
        if ($petRelative -match '(^|[\\/])(bin|obj|\.vs|\.git)([\\/]|$)') { continue }
        if ($petFile.Extension -notin @('.cs','.csproj','.manifest','.pubxml')) { continue }
        Copy-PetSourceFile $petRelative
    }
}

# Use the runtime manifest as the image allowlist; old and rejected sheets stay local.
Copy-PetSourceFile 'assets\character.json'
$petCharacter = Get-Content -LiteralPath (Join-Path $petRoot 'assets\character.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petPoseFiles = $petCharacter.poses.PSObject.Properties.Value.file | Sort-Object -Unique
foreach ($petImage in $petPoseFiles) { Copy-PetSourceFile ('assets\' + $petImage) }
foreach ($petAudio in @('growl','hiss-1','hiss-2','hiss-3','scared','meow','snore','satisfied')) { Copy-PetSourceFile ('assets\audio\' + $petAudio + '.wav') }
foreach ($petFile in Get-ChildItem -LiteralPath (Join-Path $petRoot '素材\音频') -Filter '*.mp3' -File -Recurse) {
    Copy-PetSourceFile $petFile.FullName.Substring($petRoot.Length + 1)
}
foreach ($petTool in @('build.ps1','package.ps1','prepare-github.ps1','import_audio.py','preview_gifs.py','requirements.txt')) { Copy-PetSourceFile ('tools\' + $petTool) }
foreach ($petFile in Get-ChildItem -LiteralPath (Join-Path $petRoot 'docs') -Filter '*.md' -File) { Copy-PetSourceFile ('docs\' + $petFile.Name) }
foreach ($petFile in Get-ChildItem -LiteralPath (Join-Path $petRoot 'docs\images') -File) { Copy-PetSourceFile ('docs\images\' + $petFile.Name) }

# Preserve Chinese file names and messages under both Windows PowerShell 5.1 and PowerShell 7.
foreach ($petFile in Get-ChildItem -LiteralPath (Join-Path $petSource 'tools') -Filter '*.ps1' -File) {
    $petText = [System.IO.File]::ReadAllText($petFile.FullName)
    [System.IO.File]::WriteAllText($petFile.FullName, $petText, [System.Text.UTF8Encoding]::new($true))
}

[xml]$petProject = Get-Content -LiteralPath (Join-Path $petRoot 'src\Hajimi\Hajimi.csproj') -Raw -Encoding UTF8
$petVersion = [string]$petProject.Project.PropertyGroup.Version
Add-Type -AssemblyName System.IO.Compression.FileSystem
$petZip = Join-Path $petBundle ('Hajimi-source-v' + $petVersion + '.zip')
[System.IO.Compression.ZipFile]::CreateFromDirectory($petSource, $petZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

if (-not $SourceOnly) {
    $petExecutable = Join-Path $petRoot '哈基米.exe'
    if (-not (Test-Path -LiteralPath $petExecutable)) { throw 'Source ZIP completed. Run tools/package.ps1 first to prepare a release executable.' }
    [void][System.IO.Directory]::CreateDirectory($petRelease)
    Copy-Item -LiteralPath $petExecutable -Destination (Join-Path $petRelease '哈基米.exe')
    $petHash = (Get-FileHash -LiteralPath $petExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $petRelease 'SHA256SUMS.txt'), ($petHash + '  哈基米.exe' + [Environment]::NewLine), $petUtf8)
    $petReleaseNotes = @"
# 哈基米桌宠 v$petVersion

Windows x64 单文件桌宠。下载哈基米.exe 后直接运行，无需另外安装 .NET。

- 内置角色图片、声音和运行环境，使用猫咪 ICO 图标。
- 六类右键菜单：动作、性格、吃饭、声音、显示、睡眠。
- 开机自启默认关闭，在“显示 → 开机自启”中勾选。
- 完整行走帧、好猫模式、可选置顶、大小滑块与睡眠设置。
- 回收站进食仅播放动画，不删除文件。
- 叼鼠标默认关闭；启用后 Esc 可释放，空格嘎蛋并释放。

详细更新见仓库 CHANGELOG.md；SHA256 校验值见 SHA256SUMS.txt。
"@
    [System.IO.File]::WriteAllText((Join-Path $petRelease 'release-notes.md'), $petReleaseNotes, $petUtf8)
}

$petGuide = @"
# GitHub 上传文件

1. 源码/Hajimi 内的文件上传到仓库根目录；Hajimi-source-v$petVersion.zip 是同一份内容的压缩包，请先解压再上传。
2. 发布附件/哈基米.exe 和 SHA256SUMS.txt 上传到 GitHub Releases，版本标签 v$petVersion。
3. release-notes.md 可复制为 Release 说明。

源码与运行版分别存放。源码不含 bin、obj、旧 EXE、工具缓存、原始截图日志和重复图片。
后续重做上传包可运行 tools/prepare-github.ps1，指定新的 -OutputDirectory。
仓库许可证见源码目录中的 LICENSE（如果已选择许可证）。
"@
[System.IO.File]::WriteAllText((Join-Path $petBundle '上传说明.md'), $petGuide, $petUtf8)
Write-Output ('Source folder: ' + $petSource)
Write-Output ('Source ZIP: ' + $petZip)
if (-not $SourceOnly) { Write-Output ('Release attachments: ' + $petRelease) }
