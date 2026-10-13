# Install on Windows

Publish the project, create a data directory under `C:\ProgramData\SilentPrintAgent`, and run `Installer/install-service.ps1` as an administrator. The installer generates a random local API token, writes it to protected configuration, and ACLs the data and configuration for the service identity, SYSTEM, and Administrators. By default the service runs as `LocalService`; pass `-ServiceCredential (Get-Credential)` for an account that can see user-scoped network printers.

Before enabling automatic startup, run the executable interactively with the production configuration and verify `/health`, `/api/v1/printers`, and one test job under the same account. Do not delete the data directory during uninstall: it contains the persistent queue used to recover pending work.
