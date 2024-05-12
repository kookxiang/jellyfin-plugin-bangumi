Set-Location $PSScriptRoot

if ($env:PERSONAL_ACCESS_TOKEN) {
    $PersonalAccessToken = $env:PERSONAL_ACCESS_TOKEN
} elseif (Test-Path -Path "token") {
    $PersonalAccessToken = (Get-Content "token")
} else {
    Write-Host 'No Personal Access Token found'
}

$PluginList = Get-Content repository.json | ConvertFrom-Json
$PluginInfo = $PluginList[0]

$Request = Invoke-WebRequest -Headers @{"Authorization" = "token $PersonalAccessToken"} https://api.github.com/repos/kookxiang/jellyfin-plugin-bangumi/releases
$Releases = $Request.Content | ConvertFrom-Json
$targetAbi = "10.9.0.0"
foreach ($Release in $Releases) {
    $version = $Release.tag_name
    if (Test-Path -Path "release/$version.zip") {
        continue
    }
    foreach ($asset in $Release.assets) {
        if ($asset.name -notlike "Jellyfin*.zip") {
            continue
        }
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $asset.name
    }
    @{
        category = "Metadata"
        changelog = $Release.body
        description = "Jellyfin bgm.tv 数据源插件，用于拉取中文番剧信息及图片。"
        guid = "41b59f1b-a6cf-474a-b416-785379cbd856"
        name = "Bangumi"
        overview = "bgm.tv metadata provider for Jellyfin"
        owner = "kookxiang"
        targetAbi = $targetAbi
        timestamp = $Release.published_at
        version = "$version.0"
    } | ConvertTo-Json -Compress | Out-File "meta.json" -Encoding UTF8 -NoNewline
    Move-Item $asset.name "release/$version.zip"
    Compress-Archive -LiteralPath "meta.json" -Update -DestinationPath "release/$version.zip"
    Remove-Item -Path "meta.json"

    $PluginInfo.versions = @(@{
        checksum =  (Get-FileHash -Algorithm MD5 "release/$version.zip").Hash.ToLower()
        changelog = $Release.body
        targetAbi = $targetAbi
        sourceUrl = "https://jellyfin-plugin-bangumi.pages.dev/release/$version.zip"
        timestamp = $Release.published_at
        version = "$version.0"
    }) + $PluginInfo.versions
}

ConvertTo-Json @($PluginInfo) -Compress -Depth 3 | Out-File "repository.json" -Encoding UTF8 -NoNewline
