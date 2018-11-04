# Prerequisites

Nerdbank.GitVersioning comes with all required software included, for most platforms.

## Linux considerations

Because of our dependency on libgit2, certain native packages must be installed
on the operating system for Nerdbank.GitVersioning to function.

### Ubuntu 18.04 (Bionic Beaver)

Two native packages must be added manually before nb.gv will work properly:

1. libcurl3
1. libssl1.0.0

You can install these packages with the following command:

```sh
sudo apt-get install -y libcurl3 libssl1.0.0
```
