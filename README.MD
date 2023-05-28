# ProxyDot

Local worker, designed to proxy requests on a local port, to a pre-specified remote server. 

Written in C# and .Net Core.

For testing purposes used .Net Core 8 preview and AOT Publish.

Only Windows.

## Features
- Kerberos authentication is supported for the target site
- Write request logs to file (with Serilog)

## Build

Go to root project directory and:

- build with `dotnet build`
- or AOT publish with `dotnet publish`
- or just build & run in place `dotnet run`

You are absolutely free to change the .Net version and the publishing strategy in the `ProxyDot.csproj` configuration :).

## Configuration

Values configure in `appsettings.json`

```json
  "ProxyDot": {
    "LocalPort": 8001,                              # Local port (default 8081)
    "UpstreamURI": "https://www.contoso.local",     # Remote URI for proxy
    "UseDefaultCredentials": true,                  # If "true" using DefaultCredentials
    "AuthenticationMethod": "Negotiate",            # Negotiate, NTLM, Basic
    "UserName": "User",                             # Remote user name
    "Domain": "Contoso"                             # Remote domain
  },
```

Default logging configure to write to console and file in `logs/log.txt`:

## Usage

Just run `dotnet run` or `./ProxyDot.exe` if application is published. **Administrative privileges are mandatory**!

If setting `UseDefaultCredentials` sets to `false` you will be prompted for a remote's password before working.

After launching the application, you can access the remote server via a local address using your browser. postman, console utilities, etc.

``` pwsh
$Response = Invoke-WebRequest -URI http://127.0.0.1:8001/_api/accounts/100500
$Response

$Response = iwr http://127.0.0.1:8001/index.html
$Response
```

## Licensing

Copyright (c) MIT License