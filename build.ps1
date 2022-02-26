$PluginInfo = @{
    category = "Metadata"
    description = "bgm.tv metadata provider for Jellyfin"
    guid = "41b59f1b-a6cf-474a-b416-785379cbd856"
    name = "Bangumi"
    overview = "Jellyfin bgm.tv 数据源插件，用于拉取中文番剧信息及图片。"
    owner = "kookxiang"
    versions = @()
}

$Request = curl https://api.github.com/repos/kookxiang/jellyfin-plugin-bangumi/releases
$Releases = $Request.Content | ConvertFrom-Json
$targetAbi = "10.7.0.0"
foreach ($Release in $Releases) {
    $version = $Release.tag_name
    if (!(Test-Path -Path "release/$version.zip")) {
        Invoke-WebRequest -Uri $Release.assets[0].browser_download_url -OutFile $Release.assets[0].name
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
        Compress-Archive -LiteralPath $Release.assets[0].name, "meta.json" -DestinationPath "release/$version.zip"
        Remove-Item -Path $Release.assets[0].name
        Remove-Item -Path "meta.json"
    }

    $PluginInfo.versions += @{
        checksum =  (Get-FileHash -Algorithm MD5 "release/$version.zip").Hash.ToLower()
        changelog = $Release.body
        targetAbi = $targetAbi
        sourceUrl = "https://kookxiang.github.io/jellyfin-plugin-bangumi/release/$version.zip"
        timestamp = $Release.published_at
        version = "$version.0"
    }
}

ConvertTo-Json @($PluginInfo) -Compress -Depth 3 | Out-File "repository.json" -Encoding UTF8 -NoNewline
