#FROM mcr.microsoft.com/dotnet/runtime:8.0
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled-extra

# Working directory anlegen, Dateien kopieren und Berechtigungen setzen
WORKDIR /app
COPY ./tmp ./

# Umgebungsvariablen setzen
ENV "ISCI_Identifikation"="abbild"
ENV "ISCI_Ressource"="hostname"
ENV "ISCI_Anwendung"="Automatisierungssystem"
ENV "ISCI_OrdnerAnwendungen"="/app/Anwendungen"
ENV "ISCI_OrdnerDatenstrukturen"="/app/Datenstrukturen"
ENV "ISCI_influxDbAdresse"="127.0.0.1:8086"
ENV "ISCI_influxDbOrganisationId"=""
ENV "ISCI_minimaleAnzahlFuerDbUpload"=100
ENV "ISCI_pauseZwischenUploadsInMs"=100
#
# Die beiden Ordner in den Umgebungsvariablen vom Host-System müssen eingebunden werden
# Es kann eine Konfiguration ${ISCI_Identifikation}.json im Ordner "${ISCI_OrdnerAnwendungen}/${ISCI_Anwendung}/Konfigurationen" vorhanden sein

ENTRYPOINT ["./isci.abbild"]