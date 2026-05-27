########################################
## Build AppImage, move it to desktop ##
########################################

set -e

# Fresh start
rm -rf bin/Release/net10.0/linux-x64/publish

# Compile
dotnet publish -r linux-x64 -c Release --self-contained

# Copy appimage stuff
cp -r assets bin/Release/net10.0/linux-x64/publish/assets

# CD
cd bin/Release/net10.0/linux-x64/publish

# Try Pack
upx SSTool lib*audio* lib*system* || true

# Folder struct
mkdir ss
mv -t ss/ SSTool lib*audio* lib*system*

mkdir -p ss/usr/bin/
mkdir -p ss/usr/lib/
mkdir -p ss/usr/share/metainfo
mkdir -p ss/usr/share/applications

# Restructure
cp ss/SSTool ss/usr/bin/ss
cp ss/*so* ss/usr/lib/
cp 'assets/com.spessasharp.ss.appdata.xml' ss/usr/share/metainfo
cp 'assets/com.spessasharp.ss.desktop' ss/usr/share/applications/
cp 'assets/com.spessasharp.ss.desktop' ss/
cp 'assets/com.spessasharp.ss.png' ss/
cp assets/AppRun ss/

find ss/usr/lib/ -type f -exec sed -i -e 's#/usr#././#g' {} \; # >which replaces all occurrences of /usr with ././, which simply means “here”.

# Generate AppImage
[ -f assets/appimagetool-x86_64.AppImage ] || curl -L -o assets/appimagetool-x86_64.AppImage https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x assets/appimagetool-x86_64.AppImage
ARCH=x86_64 assets/appimagetool-x86_64.AppImage --no-appstream ss/

# Clear
rm -r ss/
rm -r assets/

# Create dist folder in project root
mkdir -p "$GITHUB_WORKSPACE/dist"

# Move AppImage to the dist folder
mv ss-x86_64.AppImage "$GITHUB_WORKSPACE/dist/ss"