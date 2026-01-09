#!/usr/bin/env python3
# This script converts HTML entities back to its original text, and
# sanitises the text to be used in Windows. This script is used in
# the main script, and does not need to be manually called.
#
# Example: &#12394; >> な

import html, sys, re
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')

for line in sys.stdin:
    pattern = r'[\\/:*?"<>|]+'
    escaped_text = re.sub(pattern, "_", line)
    print(html.unescape(escaped_text), end='')