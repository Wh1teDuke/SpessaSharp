# Clean slate
./clear.sh

# Setup SpessaSynth
wget "https://github.com/oven-sh/bun/releases/download/canary/bun-linux-x64.zip"
unzip bun-linux-x64.zip
mv bun-linux-x64/bun .
rm -d bun-linux-x64/
rm bun-linux-x64.zip

chmod +x ./bun

BUN_INSTALL_CACHE_DIR=bcc
./bun init -y
./bun add spessasynth_core


# Setup SpessaSharp, SpessaSynth's dark twin
git clone --depth 1 https://github.com/Wh1teDuke/SpessaSharp.git