mkdir banks
mkdir midis

# Setup SpessaSynth
wget "https://github.com/oven-sh/bun/releases/download/canary/bun-linux-x64.zip"
unzip bun-linux-x64.zip
mv bun-linux-x64/bun .
rm -d bun-linux-x64/
rm bun-linux-x64.zip

chmod +x ./bun

BUN_INSTALL_CACHE_DIR=bcc ./bun init -y
BUN_INSTALL_CACHE_DIR=bcc ./bun add spessasynth_core


# Setup SpessaSharp, SpessaSynth's dark twin
git clone --depth 1 https://github.com/Wh1teDuke/SpessaSharp.git


# Download Sound banks

wget "https://drive.usercontent.google.com/download?export=download&id=1fvCAN_nrPtgYfTYZEJCgoWXaOkKXU0yQ&confirm=t" -O "SGM_ES8.zip"
unzip SGM_ES8.zip
mv "Shan SGM ES8A.SF2" banks/

wget "https://github.com/mrbumpy409/GeneralUser-GS/raw/refs/heads/main/GeneralUser-GS.sf2" -P banks/


# Download MIDIs
wget "https://github.com/AyHa1810/touhou-midi-collection/raw/refs/heads/main/6%20-%20Arranges/nomico%20-%20Bad%20Apple!!%20(Unknown).mid" -P midis/

wget "https://gifx.co/music/MIDI%2FCorona%2FBaby%20Baby.mid" -P midis/

wget "https://github.com/spessasus/spessasynth-demo-songs/raw/refs/heads/main/demo_songs/J-Cycle.mid" -P midis/