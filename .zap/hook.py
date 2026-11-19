# Crochet d'OWASP ZAP (option --hook de zap-api-scan.py) : ajoute à chaque requête les en-têtes de la session
# d'administrateur créée par .zap/session-admin.py. ZAP attaque ainsi aussi les routes qui demandent une connexion, la
# double authentification et le rôle d'administrateur.
import json

SESSION_FILE = "/zap/wrk/session.json"


# Appelé par zap-api-scan.py une fois ZAP démarré : une règle de remplacement par en-tête, pour toutes les requêtes.
def zap_started(zap, target):
    with open(SESSION_FILE, encoding="utf-8") as session_file:
        headers = json.load(session_file)

    for name, value in headers.items():
        zap.replacer.add_rule(
            description="Session d'administrateur : " + name,
            enabled="true",
            matchtype="REQ_HEADER",
            matchregex="false",
            matchstring=name,
            replacement=value)
