SET /p ApiKey=<NugetApiKey.txt

cd nupkg
del /Q *.*

cd ..\BibleMarkdown
dotnet pack

cd ..\nupkg

for /r %%i in (*.nupkg) do (
    dotnet nuget push %%i --api-key %ApiKey% -s https://api.nuget.org/v3/index.json --skip-duplicate
)

cd ..