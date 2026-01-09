Bigfish Games wrapID fetcher (2026) by kevinj93

Get wrapIDs of the latest games on Big Fish Games

Prerequisites:

- Python 3
- playwright
- beautifulsoup4

Setup:

After installing Python 3, run the following commands in terminal (Tested on Win11)

pip install playwright beautifulsoup4
playwright install


Customization:

Open config.ini and customize it to your liking.

config.ini supports a list of platforms and languages.

- platform: win, mac
- language: eng, ger, spa, fre, ita, jap, dut, swe, dan, por
- gen_script: Set to True if you'd like the python script to generate a execute-ready script for bfg-dl,  False if you only want a list of wrap IDs.
- latest_games_count: Number of games to fetch, sorted by newest to oldest.

How to run:

Open command prompt in the same dir as the script, then execute the following:

python get_latest_wrapids.py