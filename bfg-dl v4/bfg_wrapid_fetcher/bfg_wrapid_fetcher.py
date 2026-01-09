'''
Bigfish Games wrapID fetcher (2026) by kevinj93

Get wrapIDs of the latest games on Big Fish Games and generate a script to run with BigFish Games Downloader (bfg-dl)
'''


from playwright.sync_api import sync_playwright
from bs4 import BeautifulSoup
from urllib.parse import urljoin
import math, os, re, sys


'''
in cmd, run the following before executing this

pip install playwright beautifulsoup4
playwright install


'''
os.chdir(sys.path[0])

#Releases are sorted by release date (Newest First)

#page_nr = 1
#page_size = 100

#filters available

config = dict([e.split("=") for e in [g.strip() for g in open("config.ini","r").readlines()]])

platform = {"win":"t1","mac":"t2"}
platform_codes = {"win":"Windows%2C150","mac":"Mac%2C153"}
languages = {"eng":"l1","ger":"l2","spa":"l3","fre":"l4", "ita":"l7","jap":"l8","dut":"l10","swe":"l11","dan":"l12","por":"l13"}
lang_codes = {"eng":"English%2C114","ger":"German%2C117","spa":"Spanish%2C120","fre":"French%2C123", "ita":"Italian%2C126","jap":"Japanese%2C129","dut":"Dutch%2C135","swe":"Swedish%2C138","dan":"Danish%2C141","por":"Portuguese%2C144"}


user_platform = config["platform"]
user_language = config["language"]
user_gen_script = config["gen_script"]
user_latest_game_count = int(config["latest_games_count"])


wrapid_count_per_page = 100
pages_count = math.ceil(user_latest_game_count/wrapid_count_per_page)
#print(user_gen_script)

def get_wrap_id(filter, page_nr, page_size):

    url = f"https://www.bigfishgames.com/games.html?page={page_nr}&page_size={page_size}"
    
    # if something changes in the bigfish backend, remove lines 54 -> 56
    platform_code = "&platform[filter]=" + platform_codes[user_platform]
    lang_code = "&language[filter]=" + lang_codes[user_language]
    url += platform_code + lang_code 

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        page.goto(url, wait_until="networkidle")

        html = page.content()
        browser.close()

    soup = BeautifulSoup(html, "html.parser")

    urls = set()
    for a in soup.find_all("a", href=True):
        urls.add(urljoin(url, a["href"]))

    urls_parsed = []

    for url in urls:
        match = re.search(r'F\d+T\dL\d', url, re.IGNORECASE)
        if match:
            result = match.group(0)
            if filter in result:
                urls_parsed.append(result.upper())

    return urls_parsed


def write_to_file(url_list):
    if user_gen_script == "True":
        with open(f"wrapidlist.sh","w") as f:
            #BETTER VERSION
            for i in range(user_latest_game_count):
                f.write("bash bfg-dl.sh -d " + url_list[i] + "\n")

            #COMMENT THIS OUT
            # for parsed_url in url_list:
            #     f.write("bash bfg-dl.sh -d " + parsed_url + "\n")
    else: 
        with open(f"wrapidlist.txt","w") as f:
            for i in range(user_latest_game_count):
                f.write(url_list[i] + "\n")
            # for parsed_url in url_list:
            #     f.write(parsed_url + "\n")

def print_main():
    [print(line) for line in ["Bigfish Games wrapid fetcher (2026) by kevinj93 \n", f"platform: {user_platform}", f"language: {user_language}", f"WrapIDs to fetch: {user_latest_game_count} \n" ]]

def run():
    wrapids_all = []

    print("fetching games ...")

    for i in range(1, pages_count+1):
        wrapids = get_wrap_id(platform[user_platform] + languages[user_language], i, wrapid_count_per_page)
        wrapids_all += wrapids

    print("Writing to file ...")
    write_to_file(wrapids_all)
    print("Done!")

print_main()
run()