# Building Onfinity from source

The released packages on [SourceForge](https://sourceforge.net/projects/erp-crm-advant/files/) are the quick way to a running system; this page is for building the application from this repository.

## What you need

- Windows with IIS and the .NET Framework (4.5 or later; current builds target 4.8), and Visual Studio 2019 or later.
- A database: PostgreSQL 12 or later, or Oracle 12c or later, loaded from the database files of the release that matches this code. The database and hosting files are in the [release folders on SourceForge](https://sourceforge.net/projects/erp-crm-advant/files/).
- The three repositories, built in order: [Official-VABaseFiles](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VABaseFiles), [Official-VAFramework](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VAFramework), then this one.

## Steps

1. Clone or download this repository and open `ViennaAdvantageWeb.sln` in Visual Studio.
2. The web project (`ViennaAdvantageWeb`) needs two folders that are not in the repository: `DLL` (the compiled base and framework libraries) and `Areas` (the application areas of the modules installed in your database). Create both under `ViennaAdvantageWeb` if they do not exist.
3. Build [Official-VABaseFiles](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VABaseFiles) and [Official-VAFramework](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VAFramework), or take the compiled libraries from the partner kit that matches your database version, and put them into `DLL`. The versions must match the *Vienna Advantage Base Files* and *Vienna Advantage Framework* module versions shown in your database: log in with the System Administration role, open **Module Management** from the menu, and search for the two modules.
4. Copy each installed module's area folder into `Areas`, again for the versions your database reports.
5. In `ViennaAdvantageWeb/Web.config`, set the connection string under `appSettings` for your database: `postgresqlConnectionString` or `oracleConnectionString`.
6. Build, host the web project in IIS, and log in.

The partner kit (compiled libraries and areas per release) is available to registered members through the [community portal](https://login.onfinity.io/register.aspx).

## Further reading

- [Development guide](https://viennaadvantage.atlassian.net/wiki/spaces/VA/pages/9207809/Development+Guide)
- [Installing modules from the Onfinity Market](https://viennaadvantage.atlassian.net/wiki/spaces/VA/pages/3670266/About+VIENNA+Advantage+Market)
- [Release notes](https://viennaadvantage.atlassian.net/wiki/spaces/VA/pages/1769505/Release+Notes)
