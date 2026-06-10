# Refer to https://hub.docker.com/_/microsoft-dotnet-sdk for available versions
FROM mcr.microsoft.com/dotnet/sdk:10.0.301@sha256:548d93f8a18a1acbe6cc127bc4f47281430d34a9e35c18afa80a8d6741c2adc3

# Installing mono makes `dotnet test` work without errors even for net472.
# But installing it takes a long time, so it's excluded by default.
#RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
#RUN echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | tee /etc/apt/sources.list.d/mono-official-stable.list
#RUN apt-get update
#RUN DEBIAN_FRONTEND=noninteractive apt-get install -y mono-devel

# Clear the NUGET_XMLDOC_MODE env var so xml api doc files get unpacked, allowing a rich experience in Intellisense.
# See https://github.com/dotnet/dotnet-docker/issues/2790 for a discussion on this, where the prioritized use case
# was *not* devcontainers, sadly.
ENV NUGET_XMLDOC_MODE=
