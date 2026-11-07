SET PackageVersion=4.4.0
SET Configuration=Release

del nupkg\*.nupkg
del nupkg\*.snupkg

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion%

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion% ^
    -p:DebugType=portable ^
    -p:DebugSymbols=true ^
    -p:IncludeSymbols=true ^
    -p:SymbolPackageFormat=snupkg

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion% ^
    -p:PackAsTool=true

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion% ^
    -p:PackAsTool=true ^
    -p:DebugType=portable ^
    -p:DebugSymbols=true ^
    -p:IncludeSymbols=true ^
    -p:SymbolPackageFormat=snupkg