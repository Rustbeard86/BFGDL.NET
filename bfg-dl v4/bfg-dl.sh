#!/bin/bash
VERSION="v4-20220818"

# binscentral is usually the fastest, feel free to try a different mirror
#downloadUrl="http://acdn.bigfishgames.com/downloads/"
downloadUrl="http://binscentral.bigfishgames.com/downloads/"
#downloadUrl="http://binswest.bigfishgames.com/downloads/"

usage() { echo "bfg-dl, a Big Fish Games downloader.
Usage: $0 [-e [-d [-j N]]] [wrapID [wrapID...]]

With no flags, bfg-dl will output an aria2-compatible list of links.

Options:
         -h    |  Displays this message
         -e    |  Fetches links using installers in current directory
         -d    |  Download links after fetching
         -j N  |  [default: 8] Sets downloads threads (-d required)
         -v    |  Get version

Examples:
          Fetch links to three games
          $0 F15533T1L2 F7028T1l1 F1T1L1

          Download one game with 4 download threads
          $0 -d -j4 F5260T1L1

          Download games from text file
          $0 -d \$(cat wrapidlist.txt)

          Download games using installers in current directory
          $0 -e -d" 1>&2; exit 1; }

while getopts "j:dhve" o; do
    case "${o}" in
        d)
            d=1
            ;;
        j)
            j=${OPTARG}
            reg='^[0-9]+$'
            if ! [[ $j =~ $reg ]] ; then
              echo "Error: Not a number" >&2; exit 1
            fi
            if [ -z "$d" ]; then echo "Error: -d must be called before -j"; exit 1; fi
            ;;
        h)
            usage
            ;;
        v)
            echo $VERSION
            exit 0
            ;;
        e)
            wrapID=$(ls | grep l1_gF | cut -d'_' -f4 | sed 's/g//g')
            if [ -z "$wrapID" ]; then echo "Error: no valid installers found"; exit 1; fi
            ;;
    esac
done

if [ -z "$j" ]; then
  j=8
fi

shift $((OPTIND-1))

if [ -z "$wrapID" ]; then
:
else
set -- "$wrapID"
fi

if [ "$#" == 0 ]; then
echo "Error: Missing wrapID(s)
Usage: $0 wrapID [wrapID ...]

Try '$0 -h'"
exit 1
fi

for i in $@
do
trap 'exit 130' SIGINT
trap 'exit 143' SIGTERM
response=$(curl -s -H 'Content-Type: application/xml' \
-XPOST https://shop.bigfishgames.com/rest/V1/bfg/rpc/xml \
-d "<?xml version=\"1.0\" encoding=\"utf-8\"?>
<methodCall>
 <methodName>gms.getGameInfo</methodName>
 <params>
  <param>
   <value>
    <struct>
     <member>
      <name>gameWID</name>
      <value>
       <string>$i</string>
      </value>
     </member>
     <member>
      <name>siteID</name>
      <value>
       <string>1</string>
      </value>
     </member>
     <member>
      <name>languageID</name>
      <value>
       <string>1</string>
      </value>
     </member>
     <member>
      <name>email</name>
      <value>
       <string>gamemanager@bigfishgames.com</string>
      </value>
     </member>
     <member>
      <name>extData</name>
      <value>
       <string></string>
      </value>
     </member>
     <member>
      <name>downloadID</name>
      <value>
       <string>123456789</string>
      </value>
     </member>
    </struct>
   </value>
  </param>
 </params>
</methodCall>")
gameNameAndId=$(printf '%s\n' "$response" | xq -r --arg downloadUrl "$downloadUrl" '"\(.methodResponse.params.param.value.struct.member[] | select(.name=="gameInfo") | .value.struct.member[] | select(.name=="id") |.value.string) - \(.methodResponse.params.param.value.struct.member[] | select(.name=="gameInfo") | .value.struct.member[] | select(.name=="name") |.value.string)"' | python unescapeAndSanitise.py | python unescapeAndSanitise.py)
if [ "$d" == "1" ]; then echo $gameNameAndId; else echo "# $gameNameAndId"; fi
printf '%s\n' "$response" | xq -r --arg downloadUrl "$downloadUrl" '.methodResponse.params.param.value.struct.member[]|select(.name=="downloadInfo")|.value.struct.member[]|select(.name=="segmentList")|.value.array.data.value|if type=="array" then .[]|{"fileSegmentName":.struct.member[]|select(.name=="fileSegmentName")|.value.string,"urlName":.struct.member[]|select(.name=="urlName")|.value.string}|($downloadUrl+.urlName+"\n out="+.fileSegmentName)else{"fileSegmentName":.struct.member[]|select(.name=="fileSegmentName")|.value.string,"urlName":.struct.member[]|select(.name=="urlName")|.value.string}|$downloadUrl+.urlName+"\n out="+.fileSegmentName end'| sed '/.demo./d' | if [[ "$d" == 1 ]]; then aria2c -j$j --download-result=hide --summary-interval=0 -c -i- -d "$gameNameAndId"; else cat; fi
done