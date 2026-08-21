:: ########################################
:: ##    Build exe, move it to desktop   ##
:: ########################################


:: # Compile
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true -p:PublishAot=false

:: # CD
cd bin\Release\net10.0\win-x64\publish

:: # Copy to Desktop
copy SSTool.exe "%USERPROFILE%\Desktop\ss.exe"
