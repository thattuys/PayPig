# PayPig

A scaffolded [Dalamud](https://github.com/goatcorp/Dalamud) plugin for FFXIV.

## Structure

```
PayPig/
├── PayPig.sln
├── .gitignore
└── PayPig/
    ├── PayPig.csproj        # Dalamud.NET.Sdk project
    ├── PayPig.json          # plugin manifest
    ├── Plugin.cs            # entry point (IDalamudPlugin)
    ├── Configuration.cs     # persisted settings
    └── Windows/
        ├── MainWindow.cs    # /paypig
        └── ConfigWindow.cs  # /paypig config
```

## Build

```pwsh
dotnet build PayPig/PayPig.csproj -c Debug
```

`Dalamud.NET.Sdk` resolves the Dalamud reference assemblies from
`%AppData%\XIVLauncher\addon\Hooks\dev`. If yours live elsewhere, set the
`DALAMUD_HOME` environment variable to that folder.

The build output is `PayPig\bin\Debug\PayPig.dll` (alongside the generated
`PayPig.json` manifest). Add **that folder** under **Dalamud Settings →
Experimental → Dev Plugin Locations**, then enable PayPig in `/xlplugins`
(Dev Tools → see installed dev plugins).

## Commands

- `/paypig` — open the main window
- `/paypig config` — open settings
