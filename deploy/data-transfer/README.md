# One-off data transfer

`felcra-local-20261127.tar.enc` holds a full database dump (`felcra-local.dump`) and the photos
(`photos.tar.gz`) from the test system, encrypted with AES-256. The passphrase is **not** in this repository; get it
from the project owner.

On the VPS:

```bash
cd /srv/felcra/app && git pull
mkdir -p /srv/felcra/backups/production && cd /srv/felcra/backups/production
openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 \
  -in /srv/felcra/app/deploy/data-transfer/felcra-local-20261127.tar.enc | tar -xvf -   # asks for the passphrase
sha256sum felcra-local.dump photos.tar.gz
# 4bf6663f6c52e535d27ef5e0970070ffe09ab185238277634475aa870dd75227  felcra-local.dump
# 91b35773031871d3d9c258baece341254934b2afa57632ef035f33ab12ea1b0b  photos.tar.gz

cd /srv/felcra/app/deploy
./restore.sh production /srv/felcra/backups/production/felcra-local.dump /srv/felcra/backups/production/photos.tar.gz
```

The restore replaces all data of the production stack. Afterwards sign in with the test system's accounts and change
their passwords. Then delete this folder from the repository.
