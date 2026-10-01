$ErrorActionPreference = 'Stop'
$petWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petProject = Join-Path $petWorkspace 'src\Hajimi\Hajimi.csproj'
$petOutput = Join-Path $petWorkspace 'release\Hajimi-0.4-single'
$petPublished = Join-Path $petOutput 'Hajimi.exe'
$petDestination = Join-Path $petWorkspace '哈基米.exe'

& dotnet publish $petProject '-p:PublishProfile=Standalone' '-o' $petOutput
if ($LASTEXITCODE -ne 0) { throw '发布失败，未更新根目录 EXE。' }
Copy-Item -LiteralPath $petPublished -Destination $petDestination -Force
Write-Output ('单文件 EXE：' + $petDestination)
Write-Output ('大小：' + [Math]::Round((Get-Item -LiteralPath $petDestination).Length / 1MB, 1) + ' MB')
