xnbcli for Romestead Map Workshop
=================================

unpack.bat / pack.bat alone are NOT enough — they require xnbcli.exe in this folder.

Install the executable (one time):

  cd romestead_modding
  .\tools\install-xnbcli.ps1

Or download manually:
  https://github.com/LeonBlade/xnbcli/releases/download/v1.0.7/xnbcli-windows-x64.zip
  Extract xnbcli.exe (and any DLLs next to it) into this folder.

Map Workshop "Convert XNB to PNG" runs:
  xnbcli.exe unpack <input.xnb> <output.png>
