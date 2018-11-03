#!/bin/bash

if hash gfind 2>/dev/null; then
    # OSX
    PkgFileName=$(gfind deployables/*nupkg -printf "%f\n")
else
    # Linux
    PkgFileName=$(find deployables/*nupkg -printf "%f\n")
fi

NBGV_NuGetPackageVersion=$([[ $PkgFileName =~ Nerdbank.GitVersioning\.(.*)\.nupkg ]] && echo "${BASH_REMATCH[1]}")
dotnet new classlib -o lib
dotnet add lib package nerdbank.gitversioning -s deployables -v $NBGV_NuGetPackageVersion
dotnet build lib
