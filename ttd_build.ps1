# manual script for now because we need win machine to build this in pipeline
dotnet build ./src/Nerdbank.GitVersioning.sln  -c Release /t:build,pack
New-Item -Path "./nuget_packages" -ItemType Directory 
Get-ChildItem -Path "./bin/*" -Include *.nupkg -Recurse | Copy-Item -Destination "./nuget_packages/"
dotnet nuget push .\nuget_packages\*.nupkg -k $NEXUS_NUGET_KEY -s https://nexus.adsrvr.org/repository/ttd-nuget