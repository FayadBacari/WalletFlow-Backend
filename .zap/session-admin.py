#!/usr/bin/env python3
# Crée la session d'administrateur avec laquelle OWASP ZAP attaque l'API dans la CI (voir l'étape « Attaquer l'API en
# marche » de .github/workflows/ci.yml). Sans session, ZAP n'atteint que les routes publiques : toutes les autres répondent
# 401, et seraient donc attaquées sans jamais être lues.
#
# Deux temps, sur la même base de test :
#   creer      sur l'API en développement, qui écrit ses e-mails dans un dossier : inscrit un compte jetable, le confirme
#              avec le lien de l'e-mail, puis active sa double authentification. Écrit le compte (adresse, mot de passe,
#              secret) dans un fichier. Le rôle d'administrateur se donne ensuite par SQL, avec fn_changer_role.
#   connecter  sur l'API en production : connecte le compte avec un code de double authentification, vérifie qu'il atteint
#              l'administration, puis écrit les en-têtes de la session (cookies et jeton anti-CSRF) dans un fichier, que le
#              crochet de ZAP (.zap/hook.py) ajoute à chaque requête.
#   verifier   après l'attaque : vérifie que la session est encore ouverte. Fermée en route, elle aurait laissé la fin de
#              l'attaque sans connexion, et donc arrêtée aux refus.
#
# Les deux API doivent recevoir la même clé TwoFactorSecretEncryptionKey : le secret de la double authentification est
# chiffré en base. Seule la bibliothèque standard de Python sert. Rien de secret n'est écrit sur la sortie : le compte et la
# session ne vont que dans leurs fichiers.
import argparse
import base64
import glob
import hashlib
import hmac
import json
import os
import re
import secrets
import struct
import sys
import time
import urllib.error
import urllib.request

PERIOD_IN_SECONDS = 30
CODE_LENGTH = 6
EMAIL_WAIT_IN_SECONDS = 30
FORWARDED_PROTOCOL_HEADER = "X-Forwarded-Proto"


# Arrête le script sur une erreur, en disant laquelle.
def fail(message):
    print("ÉCHEC : " + message, file=sys.stderr)
    sys.exit(1)


# Envoie une requête à l'API et renvoie le statut, les cookies posés et le corps JSON. L'API de la CI n'écoute qu'en HTTP,
# comme derrière son reverse proxy : la requête le dit donc arrivée en HTTPS, comme le proxy, que l'API croit depuis
# l'adresse locale. Sans cela, l'API refuse le jeton anti-CSRF. Les cookies sont lus et renvoyés à la main : l'API ne les pose
# qu'avec Secure et le préfixe __Host-, qu'un client refuse sur HTTP.
def call(api, method, path, body=None, cookies=None, csrf_token=None, expected=200):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(api + path, data=data, method=method)
    request.add_header(FORWARDED_PROTOCOL_HEADER, "https")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    if cookies:
        request.add_header("Cookie", "; ".join(name + "=" + value for name, value in cookies.items()))
    if csrf_token:
        request.add_header("X-CSRF-TOKEN", csrf_token)

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status, headers, text = response.status, response.headers, response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        status, headers, text = error.code, error.headers, error.read().decode("utf-8")

    if status != expected:
        fail(method + " " + path + " a répondu " + str(status) + " au lieu de " + str(expected) + " : " + text[:300])

    return read_cookies(headers), json.loads(text) if text else None


# Lit les cookies posés par une réponse : le nom et la valeur de chacun. Un cookie vidé est retiré.
def read_cookies(headers):
    cookies = {}
    for header in headers.get_all("Set-Cookie") or []:
        name, _, value = header.split(";", 1)[0].partition("=")
        cookies[name.strip()] = value.strip()
    return cookies


# Garde les cookies reçus, sans ceux que la réponse a vidés.
def merge_cookies(cookies, received):
    merged = dict(cookies)
    merged.update(received)
    return {name: value for name, value in merged.items() if value}


# Calcule le code de double authentification d'une période, comme une application d'authentification (RFC 6238, SHA-1).
def authenticator_code(secret, period):
    clean_secret = secret.replace(" ", "").upper()
    key = base64.b32decode(clean_secret + "=" * (-len(clean_secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", period), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    number = struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF
    return str(number % 10 ** CODE_LENGTH).zfill(CODE_LENGTH)


# Donne la période de 30 secondes en cours.
def current_period():
    return int(time.time()) // PERIOD_IN_SECONDS


# Attend l'e-mail de confirmation dans le dossier des e-mails de l'API en développement, et renvoie le jeton de son lien.
def wait_for_confirmation_token(emails_folder):
    deadline = time.time() + EMAIL_WAIT_IN_SECONDS
    while time.time() < deadline:
        for path in sorted(glob.glob(os.path.join(emails_folder, "*.txt"))):
            with open(path, encoding="utf-8") as email_file:
                match = re.search(r"/confirm-email#token=([A-Za-z0-9_\-]+)", email_file.read())
            if match:
                return match.group(1)
        time.sleep(0.5)
    fail("aucun e-mail de confirmation dans " + emails_folder + " après " + str(EMAIL_WAIT_IN_SECONDS) + " secondes.")


# Écrit un fichier lisible seulement par son propriétaire, et par le conteneur de ZAP quand c'est la session.
def write_json(path, content, mode):
    with open(path, "w", encoding="utf-8") as output:
        json.dump(content, output)
    os.chmod(path, mode)


# Premier temps, sur l'API en développement : crée et confirme le compte, puis active sa double authentification.
def create(arguments):
    email = "zap-admin-" + secrets.token_hex(4) + "@walletflow.test"
    password = "Zap-" + secrets.token_urlsafe(24)

    call(arguments.api, "POST", "/api/account/register", {"email": email, "password": password})
    token = wait_for_confirmation_token(arguments.emails)
    call(arguments.api, "POST", "/api/account/confirm-email", {"token": token, "password": password})

    cookies, login = call(arguments.api, "POST", "/api/account/login", {"email": email, "password": password})
    if login["isTwoFactorCodeRequired"]:
        fail("le compte neuf demande déjà un code de double authentification.")
    received, csrf = call(arguments.api, "GET", "/api/account/csrf-token", cookies=cookies)
    cookies = merge_cookies(cookies, received)

    received, activation = call(
        arguments.api, "POST", "/api/account/two-factor/start-activation", {"password": password},
        cookies=cookies, csrf_token=csrf["csrfToken"])
    cookies = merge_cookies(cookies, received)
    period = current_period()
    call(arguments.api, "POST", "/api/account/two-factor/finish-activation",
         {"code": authenticator_code(activation["secret"], period)}, cookies=cookies, csrf_token=csrf["csrfToken"])

    write_json(arguments.account, {"email": email, "password": password, "secret": activation["secret"],
                                   "activationPeriod": period}, 0o600)
    print("Compte créé et confirmé, double authentification active : " + email)


# Second temps, sur l'API en production : connecte le compte, vérifie qu'il atteint l'administration, et écrit la session.
def connect(arguments):
    with open(arguments.account, encoding="utf-8") as account_file:
        account = json.load(account_file)

    cookies, login = call(arguments.api, "POST", "/api/account/login",
                          {"email": account["email"], "password": account["password"]})
    if not login["isTwoFactorCodeRequired"]:
        fail("la connexion ne demande pas de code : la double authentification n'est pas active.")

    # Un code déjà utilisé est refusé : celui de l'activation ne sert pas deux fois. L'API accepte aussi le code de la
    # période suivante, pour un léger décalage des horloges.
    period = max(current_period(), account["activationPeriod"] + 1)
    received, _ = call(arguments.api, "POST", "/api/account/login/two-factor",
                       {"code": authenticator_code(account["secret"], period)}, cookies=cookies)
    cookies = merge_cookies(cookies, received)

    received, csrf = call(arguments.api, "GET", "/api/account/csrf-token", cookies=cookies)
    cookies = merge_cookies(cookies, received)
    call(arguments.api, "GET", "/api/admin/statistics", cookies=cookies)

    write_json(arguments.session, {
        "Cookie": ";".join(name + "=" + value for name, value in cookies.items()),
        "X-CSRF-TOKEN": csrf["csrfToken"],
        FORWARDED_PROTOCOL_HEADER: "https"
    }, 0o644)
    print("Session d'administrateur prête : " + ", ".join(sorted(cookies)) + " et le jeton anti-CSRF.")


# Après l'attaque, sur l'API en production : vérifie que la session atteint encore l'administration.
def verify(arguments):
    with open(arguments.session, encoding="utf-8") as session_file:
        headers = json.load(session_file)

    cookies = dict(pair.split("=", 1) for pair in headers["Cookie"].split(";"))
    call(arguments.api, "GET", "/api/admin/statistics", cookies=cookies, csrf_token=headers["X-CSRF-TOKEN"])
    print("La session d'administrateur a tenu pendant toute l'attaque.")


# Lit la commande et ses réglages.
def main():
    parser = argparse.ArgumentParser(description="Crée la session d'administrateur d'OWASP ZAP.")
    commands = parser.add_subparsers(dest="command", required=True)

    create_command = commands.add_parser("creer", help="sur l'API en développement")
    create_command.add_argument("--api", required=True, help="adresse de l'API, comme http://localhost:8081")
    create_command.add_argument("--emails", required=True, help="dossier où l'API écrit ses e-mails")
    create_command.add_argument("--account", required=True, help="fichier où écrire le compte")
    create_command.set_defaults(run=create)

    connect_command = commands.add_parser("connecter", help="sur l'API en production")
    connect_command.add_argument("--api", required=True, help="adresse de l'API, comme http://localhost:8080")
    connect_command.add_argument("--account", required=True, help="fichier du compte, écrit par creer")
    connect_command.add_argument("--session", required=True, help="fichier où écrire les en-têtes de la session")
    connect_command.set_defaults(run=connect)

    verify_command = commands.add_parser("verifier", help="après l'attaque, sur l'API en production")
    verify_command.add_argument("--api", required=True, help="adresse de l'API, comme http://localhost:8080")
    verify_command.add_argument("--session", required=True, help="fichier des en-têtes de la session, écrit par connecter")
    verify_command.set_defaults(run=verify)

    arguments = parser.parse_args()
    arguments.run(arguments)


if __name__ == "__main__":
    main()
