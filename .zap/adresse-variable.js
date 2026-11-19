// Script d'OWASP ZAP (type httpsender), chargé par .zap/hook.py : donne à chaque requête de ZAP une adresse IP tirée au hasard
// dans 10.0.0.0/8, dans l'en-tête X-Forwarded-For. L'API croit cet en-tête quand il vient de l'adresse locale, comme de son
// reverse proxy : l'attaque vient donc de milliers d'adresses, et les limites de débit par adresse ne l'arrêtent plus. Sans
// cela, plus de la moitié des requêtes de l'attaque recevaient 429. Les limites par session ou par utilisateur restent, et
// chaque limite garde ses propres tests.

// Tire une adresse au hasard dans 10.0.0.0/8.
function randomAddress() {
  return "10." + randomByte(0) + "." + randomByte(0) + "." + randomByte(1);
}

// Tire un nombre entier entre ce minimum et 254.
function randomByte(minimum) {
  return minimum + Math.floor(Math.random() * (255 - minimum));
}

// Appelé par ZAP avant l'envoi de chaque requête.
function sendingRequest(msg, initiator, helper) {
  msg.getRequestHeader().setHeader("X-Forwarded-For", randomAddress());
}

// Appelé par ZAP à chaque réponse : rien à faire.
function responseReceived(msg, initiator, helper) {
}
