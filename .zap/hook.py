# Crochet d'OWASP ZAP (option --hook de zap-api-scan.py) : ajoute à chaque requête les en-têtes de la session
# d'administrateur créée par .zap/session-admin.py, et une adresse IP tirée au hasard (.zap/adresse-variable.js). ZAP attaque
# ainsi aussi les routes qui demandent une connexion, la double authentification et le rôle d'administrateur, sans que les
# limites de débit par adresse n'arrêtent l'attaque.
import json

SESSION_FILE = "/zap/wrk/session.json"
ADDRESS_SCRIPT = "adresse-variable.js"
SCRIPT_ENGINES = ["Oracle Nashorn", "Graal.js"]


# Appelé par zap-api-scan.py une fois ZAP démarré : une règle de remplacement par en-tête de la session, pour toutes les
# requêtes, puis le script de l'adresse variable, avec le même moteur JavaScript que les scripts de zap-api-scan.py.
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

    available_engines = [engine.split(" : ")[1] for engine in zap.script.list_engines]
    engine = next(name for name in SCRIPT_ENGINES if name in available_engines)
    zap.script.load(
        scriptname=ADDRESS_SCRIPT,
        scripttype="httpsender",
        scriptengine=engine,
        filename="/zap/wrk/" + ADDRESS_SCRIPT)
    zap.script.enable(ADDRESS_SCRIPT)
    print("Adresse IP variable branchée, avec le moteur " + engine + ".")


# Appelé par zap-api-scan.py juste avant l'arrêt de ZAP : écrit ce que l'attaque a fait, pour prouver ce qu'elle a couvert. Les
# réponses par code disent si la session a tenu (401) et si la limite de débit a refusé une partie de l'attaque (429).
# L'avancement dit si l'attaque a fini, ou si sa durée maximale l'a coupée. Une lecture ratée est écrite, sans arrêter ZAP.
def zap_pre_shutdown(zap):
    try:
        codes = zap.stats.all_sites_stats(keyprefix="stats.code")
        print("Réponses de l'API pendant toute l'analyse, par code : " + json.dumps(codes, sort_keys=True))
        for scan in zap.ascan.scans:
            print("Attaque " + str(scan.get("id")) + " : état " + str(scan.get("state")) + ", avancement "
                  + str(scan.get("progress")) + " %, " + str(scan.get("reqCount")) + " requêtes.")
    except Exception as error:
        print("Les chiffres de ZAP n'ont pas pu être lus : " + repr(error))
