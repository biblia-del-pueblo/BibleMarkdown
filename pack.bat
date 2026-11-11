SET PackageVersion=5.0.0
SET Configuration=Release

del nupkg\*.nupkg
del nupkg\*.snupkg

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:PackAsTool=false ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion%

dotnet pack BibleMarkdown.slnx -c %Configuration% ^
    -p:Version=%PackageVersion% ^
    -p:FileVersion=%PackageVersion%.0 ^
    -p:AssemblyVersion=%PackageVersion% ^
    -p:PackAsTool=false ^
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