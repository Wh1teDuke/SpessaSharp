# Setup SpessaSynth

wget -q "https://github.com/oven-sh/bun/releases/download/canary/bun-linux-x64.zip"
unzip -q bun-linux-x64.zip
mv bun-linux-x64/bun .
rm -d bun-linux-x64/
rm bun-linux-x64.zip

chmod +x ./bun

BUN_INSTALL_CACHE_DIR=bcc ./bun init -y --silent > /dev/null 2>&1
BUN_INSTALL_CACHE_DIR=bcc ./bun add spessasynth_core --silent > /dev/null 2>&1