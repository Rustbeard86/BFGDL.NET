Requirements:
Windows only: Cygwin bash, Git Bash, or some equivalent
Python 3
xq (pip install yq)
jq (https://github.com/stedolan/jq/releases)
aria2 (https://github.com/aria2/aria2/releases)
curl

Features:
Fetch and download links for any specified game(s)
Multi-threaded downloads
Download from installers in directory* or text file
More to come...?
* - Filenames must be the same as it was when downloaded from Big Fish Games originally.

How to install the requirements for Windows users:
Install Cygwin with only base packages. (Just click Next on the package install screen.)
https://www.cygwin.com/setup-x86_64.exe
 
Install Python (check the Add to PATH option), and xq (pip install yq).
https://www.python.org/downloads
 
Download the rest of the requirements into PATH.
Tip: rundll32.exe sysdm.cpl,EditEnvironmentVariables
EXTRACT "aria2c.exe" https://github.com/aria2/aria2/releases ... build1.zip
SAVE AS "jq.exe"     https://github.com/stedolan/jq/releases ... -win64.exe
 
Open Cygwin64 Terminal and run the script from there.
Tip: cd /cygdrive/c/Users/$USER/Downloads

Usage:
With no flags, bfg-dl will only output an aria2-compatible list of links.
 
Options:
         -h    |  Displays this message
         -e    |  Fetches links using installers in current directory
         -d    |  Download links after fetching
         -j N  |  [default: 8] Sets downloads threads (-d required)
         -v    |  Get version
 
Examples:
          Fetch links to three games
          ./bfg-dl.sh F15533T1L2 F7028T1l1 F1T1L1
 
          Download one game with 4 download threads
          ./bfg-dl.sh -d -j4 F5260T1L1
 
          Download games from text file
          ./bfg-dl.sh -d $(cat wrapidlist.txt)
 
          Download games from installers in current directory
          ./bfg-dl.sh -e -d
		  
Changelog:
Each release will have an updated game list.
 
v4 | <!> WINDOWS USERS <!>
   | You now must use Cygwin's terminal or some equivalent to use this script.
   | Busybox's ash shell can not handle Unicode properly, making titles that
   | use it (such as Japanese titles) to fail. This issue doesn't occur on Linux.
   |
   | Added "!Change Install Directory.reg" as a bonus.
   | You can now fetch/download games using installers in your directory using `-e`.
   | Updated unescapeAndSanitise.py as to work properly on Windows. (Cygwin)
 
v3 | Added unescapeAndSanitise.py, a script that fixes folder names
   | Games will now download into separate folders
   | Removed demo installers
 
v2 | Added downloading capabilities
 
v1 | Initial release

Hereby a very fast bigFish Games wrapID fetcher that uses modern libraries and is very fast. It can be used to generate a script necessary for downloading the bigFish Games offline installers using bfg-dl

I included the modified bfg-dl.sh that was necessary for the script to work after bigFish updated their backend. Thanks com1100 for the solution.

After installing Python 3, run the following commands in terminal/command prompt (Tested on Win11, should also work on Linux systems.)

pip install playwright beautifulsoup4
playwright install

How to run?

In the same dir as the script, open command prompt, then execute the following:
python get_latest_wrapids.py

