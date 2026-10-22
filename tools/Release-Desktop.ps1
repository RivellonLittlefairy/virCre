#requires -Version 7.0
<#
.SYNOPSIS
Prepare, commit, build and publish a desktop release from main.
.EXAMPLE
.\tools\Release-Desktop.ps1 -Version 0.3 -Changes '修复应用窗口问题','调整设置界面'
.EXAMPLE
.\tools\Release-Desktop.ps1 -Version 0.3 -Resume
.LINK
../docs/releasing.html
#>
[CmdletBinding(SupportsShouldProcess,ConfirmImpact='Low')]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+(?:\.\d+)?$')][string]$Version,
    [string[]]$Changes,
    [string[]]$Include,
    [switch]$Resume,
    [switch]$RebuildRuntime,
    [switch]$DeepVerify
)
$ErrorActionPreference='Stop'
$repoDirectory=Split-Path $PSScriptRoot -Parent
$tag="v$Version"
$stateDirectory=Join-Path $repoDirectory ".cache/releases/$Version"
$receiptPath=Join-Path $stateDirectory 'release.json'
$notesPath=Join-Path $stateDirectory 'notes.md'
$buildPath=Join-Path $repoDirectory "out/installers/virCre-$Version-build.json"
$sourceRoots=@('desktop','android-agent','camera-agent','tools','packaging','third_party')
function Invoke-ReleaseNative([string]$Program,[string[]]$Arguments) {
    & $Program @Arguments
    if($LASTEXITCODE -ne 0) {throw "$Program failed ($LASTEXITCODE): $($Arguments[0])"}
}
function Get-ReleaseRecord {
    # GitHub's by-tag endpoint does not return drafts. List them explicitly.
    $records=(Invoke-ReleaseNative gh @('api',"repos/$repository/releases?per_page=100")) -join "`n" | ConvertFrom-Json
    $records | Where-Object tag_name -eq $tag | Select-Object -First 1
}
function Assert-RemoteAssets($Release,$Assets) {
    if(@($Release.assets).Count -ne $Assets.Count) {throw 'Release attachment count differs; keep the draft and inspect its assets.'}
    foreach($asset in $Assets) {
        $remote=$Release.assets | Where-Object name -eq $asset.name | Select-Object -First 1
        if(-not $remote -or $remote.state -ne 'uploaded' -or $remote.size -ne $asset.size -or $remote.digest -ne $asset.digest) {
            throw "Uploaded file does not match local package: $($asset.name). Retry with -Resume."
        }
    }
}
Push-Location $repoDirectory
try {
    foreach($tool in @('git','gh','pwsh','dotnet')) {Get-Command $tool -ErrorAction Stop | Out-Null}
    if(((Invoke-ReleaseNative git @('branch','--show-current')) -join '') -ne 'main') {throw 'Run the release from main.'}
    $remoteUrl=(Invoke-ReleaseNative git @('remote','get-url','origin')) -join ''
    if($remoteUrl -notmatch 'github\.com[:/]([^/]+/[^/]+?)(?:\.git)?$') {throw 'origin must be a GitHub repository.'}
    $repository=$Matches[1]
    $runtimeManifest=Join-Path $repoDirectory '.cache/custom-runtime/scrcpy/runtime-manifest.json'
    $cached=if(Test-Path -LiteralPath $runtimeManifest) {Get-Content -LiteralPath $runtimeManifest -Raw | ConvertFrom-Json} else {$null}
    $fingerprint=& (Join-Path $PSScriptRoot 'Get-CustomRuntimeFingerprint.ps1')
    $needsRuntime=$RebuildRuntime -or -not $cached -or $cached.buildType -ne 'release' -or $cached.androidMinApi -ne 29 -or $cached.virtualCameraMinApi -ne 36 -or $cached.sourceFingerprint -ne $fingerprint
    if(-not $PSCmdlet.ShouldProcess($repository,"Release $tag (prepare, commit, package, push and publish; runtime rebuild=$needsRuntime)")) {return}
    Invoke-ReleaseNative gh @('api',"repos/$repository",'--jq','.full_name') | Out-Null
    Invoke-ReleaseNative git @('fetch','origin','main','--tags')
    Invoke-ReleaseNative git @('merge-base','--is-ancestor','origin/main','HEAD')
    if($Resume) {
        if(-not(Test-Path -LiteralPath $receiptPath)) {throw 'No saved release state. Run once without -Resume.'}
        $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        if(-not(Test-Path -LiteralPath $notesPath -PathType Leaf)) {throw 'Saved release notes are missing; restore .cache/releases notes before resuming.'}
        $head=(Invoke-ReleaseNative git @('rev-parse','HEAD')) -join ''
        if($receipt.sourceCommit -ne $head -or $receipt.repository -ne $repository -or $receipt.version -ne $Version) {throw 'Release state differs from this checkout. Restore its commit before resuming.'}
        if(Invoke-ReleaseNative git @('status','--porcelain','--untracked-files=no')) {throw 'Tracked files changed after the release commit. Resume requires the original sources.'}
        if(Invoke-ReleaseNative git (@('ls-files','--others','--exclude-standard','--')+$sourceRoots)) {throw 'New source files appeared after the release commit. Resolve them before resuming.'}
    } else {
        if(Invoke-ReleaseNative git @('tag','--list',$tag)) {throw "$tag already exists. Use -Resume for an interrupted release, or choose a new version."}
        $Changes=@($Changes | Where-Object {-not [string]::IsNullOrWhiteSpace($_)})
        if($Changes.Count -eq 0) {throw 'Supply one or more user-facing -Changes items.'}
        $current=[Version](& (Join-Path $PSScriptRoot 'Get-ProjectVersion.ps1'))
        $next=[Version]$Version
        $currentNumeric=[Version]::new($current.Major,$current.Minor,[Math]::Max(0,$current.Build))
        $nextNumeric=[Version]::new($next.Major,$next.Minor,[Math]::Max(0,$next.Build))
        if($nextNumeric -le $currentNumeric) {throw 'Choose a version newer than VERSION.'}
        Write-Host '[1/5] Prepare version, release notes and commit'
        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $repoDirectory 'VERSION'),"$Version`n")
        $releaseDoc="docs/release-$Version.html"
        $items=($Changes | ForEach-Object {'<li>'+[Net.WebUtility]::HtmlEncode($_)+'</li>'}) -join "`n"
        $date=Get-Date -Format 'yyyy-MM-dd'
        $html=@"
<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>virCre $Version 更新说明</title>
<style>body{font:16px/1.85 system-ui,'Microsoft YaHei',sans-serif;max-width:960px;margin:36px auto;padding:0 24px;color:#263449}a{color:#2765e8}li{margin:10px 0}</style></head><body>
<h1>virCre $Version</h1><p>$date · Windows x64</p><h2>更新内容</h2><ul>$items</ul>
<h2>下载与升级</h2><p>安装版：virCre-$Version-win-x64-setup.exe；便携版：virCre-$Version-win-x64-portable.zip；校验文件：virCre-$Version-SHA256SUMS.txt。</p>
<p>升级前关闭 virCre 及其手机应用窗口。便携版需完整解压并保留整个目录；移动程序目录后需重新创建手机应用快捷方式。</p>
<p>发布流程执行构建和文件校验，不自动运行安装器或操作手机。安装包未进行代码签名。兼容要求与使用方法见 <a href="https://github.com/$repository/blob/$tag/README.md">README</a>。</p></body></html>
"@
        if(-not(Test-Path -LiteralPath (Join-Path $repoDirectory $releaseDoc))) {
            [IO.File]::WriteAllText((Join-Path $repoDirectory $releaseDoc),$html)
        }
        $readmePath=Join-Path $repoDirectory 'README.md'
        $readme=[IO.File]::ReadAllText($readmePath)
        $readme=[regex]::Replace($readme,'\[[^\]\r\n]+ 更新说明\]\(docs/release-[\d.]+\.html\)',"[$Version 更新说明]($releaseDoc)")
        [IO.File]::WriteAllText($readmePath,$readme)
        $body=(($Changes | ForEach-Object {'- '+$_}) -join "`n")+"`n`n安装版与便携版均已提供。升级前请关闭旧程序；便携版请完整解压并保留整个目录。`n`n已构建并校验分发文件，安装及设备交互由用户验证。`n`n[完整更新说明](https://github.com/$repository/blob/$tag/$releaseDoc)"
        [IO.File]::WriteAllText($notesPath,$body)
        Invoke-ReleaseNative git @('add','--update')
        # Include new source files; leave unrelated untracked document drafts alone.
        $stageArguments=@('add','--')+$sourceRoots+@('VERSION','README.md',$releaseDoc)
        if($Include) {$stageArguments+=$Include}
        Invoke-ReleaseNative git $stageArguments
        Invoke-ReleaseNative git @('diff','--cached','--check')
        Invoke-ReleaseNative git @('commit','-m',"chore: release virCre $Version")
        $receipt=[pscustomobject]@{version=$Version;repository=$repository;sourceCommit=((Invoke-ReleaseNative git @('rev-parse','HEAD')) -join '')}
        $receipt | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    }
    Write-Host '[2/5] Build packages (detailed output stays in .cache/releases)'
    $build=if(Test-Path -LiteralPath $buildPath) {Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json} else {$null}
    $canReuse=$false
    if($Resume -and $build -and -not $RebuildRuntime) {
        $inventory=Join-Path $build.publishDirectory 'compliance/manifest.json'
        if(Test-Path -LiteralPath $inventory) {
            $canReuse=(Get-Content -LiteralPath $inventory -Raw | ConvertFrom-Json).sourceCommit -eq $receipt.sourceCommit
            foreach($path in @($build.installer,$build.portable,$build.checksums)) {$canReuse=$canReuse -and (Test-Path -LiteralPath $path -PathType Leaf)}
        }
    }
    if(-not $canReuse) {
        $arguments=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'Build-Installer.ps1'),'-Version',$Version)
        if(-not $needsRuntime) {$arguments+='-SkipRuntimeBuild'}
        $log=Join-Path $stateDirectory 'build.log'
        try {Invoke-ReleaseNative pwsh $arguments *> $log} catch {throw "Package build failed; see $log. Fix the cause, then use -Resume."}
        $build=Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json
    } else {Write-Host 'Reuse completed packages from the same release commit.'}
    $inventory=Get-Content -LiteralPath (Join-Path $build.publishDirectory 'compliance/manifest.json') -Raw | ConvertFrom-Json
    if($inventory.sourceCommit -ne $receipt.sourceCommit -or $build.version -ne $Version) {throw 'Package sources/version differ from the release commit.'}
    foreach($file in @((Join-Path $build.publishDirectory 'VirCre.Desktop.exe'),$build.installer)) {
        $info=(Get-Item -LiteralPath $file).VersionInfo
        $parsed=[Version]$Version
        if($info.FileMajorPart -ne $parsed.Major -or $info.FileMinorPart -ne $parsed.Minor -or $info.FileBuildPart -ne [Math]::Max(0,$parsed.Build) -or $info.FilePrivatePart -ne 0) {throw "Incorrect version: $file"}
    }
    foreach($line in Get-Content -LiteralPath $build.checksums) {
        if($line -notmatch '^([a-f0-9]{64})  (.+)$') {throw 'Invalid SHA256SUMS file.'}
        $path=Join-Path (Split-Path $build.checksums -Parent) $Matches[2]
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) {throw "Package checksum mismatch: $path"}
    }
    if($DeepVerify) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive=[IO.Compression.ZipFile]::OpenRead($build.portable)
        try {
            $entries=@($archive.Entries | Where-Object Name -ne '')
            if($entries.Count -ne @(Get-ChildItem -LiteralPath $build.publishDirectory -File -Recurse).Count) {throw 'Portable ZIP file count differs.'}
            foreach($entry in $entries) {
                $stream=$entry.Open()
                try {$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
                if($hash -ne (Get-FileHash -LiteralPath (Join-Path $build.publishDirectory $entry.FullName) -Algorithm SHA256).Hash) {throw "ZIP entry differs: $($entry.FullName)"}
            }
        } finally {$archive.Dispose()}
    }
    if(Invoke-ReleaseNative git @('status','--porcelain','--untracked-files=no')) {throw 'Tracked files changed during packaging. Stop before pushing.'}
    if(((Invoke-ReleaseNative git @('rev-parse','HEAD')) -join '') -ne $receipt.sourceCommit) {throw 'HEAD changed during packaging.'}
    Write-Host '[3/5] Push main and the annotated tag'
    if(Invoke-ReleaseNative git @('tag','--list',$tag)) {
        if(((Invoke-ReleaseNative git @('rev-parse',"$tag^{commit}")) -join '') -ne $receipt.sourceCommit) {throw 'Existing tag points to another commit.'}
    } else {Invoke-ReleaseNative git @('tag','-a',$tag,'-m',"virCre $Version")}
    Invoke-ReleaseNative git @('push','--atomic','origin','main',"refs/tags/$tag")
    Write-Host '[4/5] Upload the three assets to a draft release'
    $assets=@($build.installer,$build.portable,$build.checksums) | ForEach-Object {
        [pscustomobject]@{path=$_;name=[IO.Path]::GetFileName($_);size=(Get-Item -LiteralPath $_).Length;digest=('sha256:'+(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())}
    }
    $release=Get-ReleaseRecord
    if(-not $release) {
        Invoke-ReleaseNative gh @('release','create',$tag,'--repo',$repository,'--verify-tag','--draft','--title',"virCre $Version",'--notes-file',$notesPath)
        $release=Get-ReleaseRecord
    }
    if($release.draft) {
        foreach($asset in $assets) {
            $remote=$release.assets | Where-Object name -eq $asset.name | Select-Object -First 1
            if($remote -and $remote.state -eq 'uploaded' -and $remote.size -eq $asset.size -and $remote.digest -eq $asset.digest) {continue}
            Invoke-ReleaseNative gh @('release','upload',$tag,$asset.path,'--repo',$repository,'--clobber')
        }
    }
    $release=Get-ReleaseRecord
    Assert-RemoteAssets $release $assets
    Write-Host '[5/5] Publish and verify the latest release'
    if($release.draft) {Invoke-ReleaseNative gh @('api','--method','PATCH',"repos/$repository/releases/$($release.id)",'-F','draft=false','-f','make_latest=true','--jq','.html_url')}
    $latest=(Invoke-ReleaseNative gh @('api',"repos/$repository/releases/latest")) -join "`n" | ConvertFrom-Json
    if($latest.tag_name -ne $tag -or $latest.draft) {throw 'Latest release was not updated. Inspect GitHub before retrying.'}
    Assert-RemoteAssets $latest $assets
    Write-Output "Published: $($latest.html_url)"
} finally {Pop-Location}
