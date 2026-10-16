#!/bin/sh
# Deploy the online relay to ruzzoli.de/mva-battles. Safe to re-run.
set -e
cd "$(dirname "$0")"
HOST=ruzzoli.de
DEST=/opt/mva-battles
GOOS=linux GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -o /tmp/mva-battles .
ssh "$HOST" "sudo mkdir -p $DEST && sudo chown \$(id -un) $DEST"
scp /tmp/mva-battles "$HOST:$DEST/mva-battles.new"
scp mva-battles.service mva-battles.nginx "$HOST:/tmp/"
ssh "$HOST" "set -e
	mv $DEST/mva-battles.new $DEST/mva-battles
	sudo install -m 644 /tmp/mva-battles.service /etc/systemd/system/mva-battles.service
	sudo install -m 644 /tmp/mva-battles.nginx /etc/nginx/snippets/mva-battles.conf
	CONF=/etc/nginx/sites-enabled/ruzzoli.de.conf
	if ! sudo grep -q 'snippets/mva-battles.conf' \$CONF; then
		sudo cp \$CONF /etc/nginx/ruzzoli.de.conf.bak-mva-battles
		sudo sed -i 's#^\\( *\\)include /etc/nginx/snippets/oxce-battles.conf;#&\\n\\1include /etc/nginx/snippets/mva-battles.conf;#' \$CONF
		sudo grep -q 'snippets/mva-battles.conf' \$CONF || { echo 'could not add the nginx include'; exit 1; }
	fi
	rm -f /tmp/mva-battles.service /tmp/mva-battles.nginx
	sudo systemctl daemon-reload
	sudo systemctl enable mva-battles
	sudo systemctl restart mva-battles
	sudo nginx -t || { sudo cp /etc/nginx/ruzzoli.de.conf.bak-mva-battles \$CONF; exit 1; }
	sudo systemctl reload nginx"
rm /tmp/mva-battles
curl -fsS -o /dev/null -w 'lobby %{http_code}\n' https://$HOST/mva-battles/api/games
