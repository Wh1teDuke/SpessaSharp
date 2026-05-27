:: ########################################
:: ##    Build exe, move it to desktop   ##
:: ########################################


:: # Compile
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true -p:PublishAot=false

:: # CD
cd bin\Release\net10.0\win-x64\publish

:: Create dist folder in project root
if not exist "%GITHUB_WORKSPACE%\dist" mkdir "%GITHUB_WORKSPACE%\dist"

:: # Copy to Desktop
copy SSTool.exe "%GITHUB_WORKSPACE%\dist\ss.exe"
