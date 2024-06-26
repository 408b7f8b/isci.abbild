﻿using System;
using System.Collections.Generic;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using isci.Allgemein;
using isci.Beschreibung;
using isci.Daten;

namespace isci.abbild
{
    class Program
    {
        public class Konfiguration(string[] args) : Parameter(args)
        {
            #pragma warning disable CS0649
            [fromArgs, fromEnv]
            public string influxDbToken;
            [fromArgs, fromEnv]
            public string influxDbAdminToken;
            [fromArgs, fromEnv]
            public string influxDbAdresse;
            [fromArgs, fromEnv]
            public string influxDbOrganisationId;
            [fromArgs, fromEnv]
            public int minimaleAnzahlFuerDbUpload;
            [fromArgs, fromEnv]
            public int pauseZwischenUploadsInMs = 15000;
            #pragma warning restore CS0649
            [fromArgs, fromEnv]
            public int influxDbBatchSize = 50000;
            [fromArgs, fromEnv]
            public int influxDbFlushIntervall = 10000;
        }

        static List<string> abbild;
        static bool zwischengespeichert_flag = false;

        static void AbbildErfassen(object state)
        {
            /*var files = System.IO.Directory.GetFiles(konfiguration.pfad);

                foreach (var f in files)
                {
                    if (!written.Contains(f))
                    {
                        var f_split = f.Substring(f.LastIndexOf('/')+1).Split('_');
                        var lprotocol = System.IO.File.ReadAllLines(f);
                        if (!buckets.Contains(f_split[0]))
                        {
                            bucketApi.CreateBucketAsync(new Bucket(name:f_split[0], orgID:konfiguration.orgId)).RunSynchronously();
                            buckets.Add(f_split[0]);
                        }
                        writeApi.WriteRecords(lprotocol, WritePrecision.Ms, bucket:f_split[0], org:konfiguration.orgId);
                        written.Add(f);
                        System.IO.File.WriteAllLines("written", written);
                    }
                }*/

            var dt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            foreach (var dateneintrag in structure.dateneinträge)
            {
                var l = $"{dateneintrag.Key},ressource={konfiguration.Ressource} value={dateneintrag.Value.WertSerialisieren()} {dt}";
                abbild.Add(l);
            }

            if (abbild.Count > konfiguration.minimaleAnzahlFuerDbUpload)
            {
                try {
                    writeApi.WriteRecords(abbild, bucket:konfiguration.Anwendung, org:konfiguration.influxDbOrganisationId, precision:WritePrecision.Ms);
                    if (zwischengespeichert_flag == true)
                    {
                        Logger.Information("Zwischengespeichertes Datenabbild wird hochgeladen.");
                        var zwischengespeichert = System.IO.File.ReadAllLines("abbild_datenpuffer");
                        writeApi.WriteRecords(zwischengespeichert, bucket:konfiguration.Anwendung, org:konfiguration.influxDbOrganisationId, precision:WritePrecision.Ms);
                        System.IO.File.WriteAllText("zwischengespeichert", "");
                        zwischengespeichert_flag = false;
                    }
                } catch (System.Exception e)
                {
                    Logger.Fehler("Ausnahme beim Schreiben des Datenabbilds: " + e.Message);
                    Logger.Information("Datenabbild wird zwischengespeichert.");
                    System.IO.File.AppendAllLinesAsync("abbild_datenpuffer", abbild, System.Threading.CancellationToken.None);
                    zwischengespeichert_flag = true;
                }
                abbild.Clear();
            }
        }

        static void Neustarten(object source, System.IO.FileSystemEventArgs e)
        {
            neustarten = true;
        }

        static bool neustarten;

        static Datenstruktur structure;
        static Konfiguration konfiguration;
        static InfluxDBClient influxDBClient;
        static InfluxDB.Client.WriteApi writeApi;
        
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            konfiguration = new Konfiguration(args);

            while (true)
            {
                abbild = new List<string>();

                structure = new Datenstruktur(konfiguration);
                var ausfuehrungsmodell = new Ausführungsmodell(konfiguration, structure.Zustand);

                var beschreibung = new Modul(konfiguration.Identifikation, "isci.abbild")
                {
                    Name = "Abbild Ressource " + konfiguration.Identifikation,
                    Beschreibung = "Modul zur Abbilderstellung gegen externe Datenbank"
                };
                beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

                structure.DatenmodelleEinhängenAusOrdner(konfiguration.OrdnerDatenmodelle);
                structure.Start();

                var watcher = new System.IO.FileSystemWatcher()
                {
                    Path = konfiguration.OrdnerDatenmodelle,
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName,
                    Filter = "*.json" // Filter for JSON files
                };

                watcher.Changed += Neustarten;
                watcher.Created += Neustarten;
                watcher.Deleted += Neustarten;
                watcher.Renamed += Neustarten;

                watcher.EnableRaisingEvents = true;

                if (System.IO.File.Exists("zwischengespeichert"))
                {
                    if (System.IO.File.ReadAllText("zwischengespeichert") != "")
                    {
                        zwischengespeichert_flag = true;
                    }
                }

                influxDBClient = InfluxDBClientFactory.Create(konfiguration.influxDbAdresse, konfiguration.influxDbToken);
                influxDBClient.SetLogLevel(InfluxDB.Client.Core.LogLevel.None);

                var writeOptions = WriteOptions
                .CreateNew()
                .BatchSize(konfiguration.influxDbBatchSize)
                .FlushInterval(konfiguration.influxDbFlushIntervall)
                .Build();

                writeApi = influxDBClient.GetWriteApi(writeOptions);

                if (konfiguration.influxDbAdminToken != "" && konfiguration.influxDbAdminToken != null)
                {
                    string orgname;
                    while(true)
                    {
                        try {
                            orgname = (await influxDBClient.GetOrganizationsApi().FindOrganizationByIdAsync(konfiguration.influxDbOrganisationId)).Name;
                            break;
                        } catch (System.Exception e)
                        {
                            Logger.Fehler("Ausnahme beim Abruf der Organisation. InfluxDB möglicherweise nicht erreichbar. " + e.Message);
                        }
                    }

                    var bucketApi = influxDBClient.GetBucketsApi();

                    List<Bucket> bucketApiOrg = new List<Bucket>();

                    try {                    
                        bucketApiOrg = await bucketApi.FindBucketsByOrgNameAsync(orgname);                    
                    }
                    catch (System.Exception e)
                    {
                        Logger.Fatal("Ausnahme beim Abruf der Buckets für die Organisation: " + e.Message);
                    }

                    var buckets = new List<string>();
                    foreach (var bucket in bucketApiOrg)
                    {
                        Logger.Debug("Gefundener Bucket " + bucket.Name);
                        buckets.Add(bucket.Name);
                    }

                    if (!buckets.Contains(konfiguration.Anwendung))
                    {
                        Logger.Information("Kein Bucket für Anwendung " + konfiguration.Anwendung + " existent. Erstelle neuen.");
                        try {
                            bucketApi.CreateBucketAsync(new Bucket(name:konfiguration.Anwendung, orgID:konfiguration.influxDbOrganisationId,
                            retentionRules:new List<BucketRetentionRules>(){
                                new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0, 60*60*24*365)
                            })).Wait();
                        } catch (System.Exception e)
                        {
                            Logger.Fatal("Ausnahme beim Erstellen des Anwendungs-Buckets: " + e.Message);
                        }
                    }
                }

                var zyklischeAusfuehrungUpload = new System.Threading.Timer(AbbildErfassen, null, 0, konfiguration.pauseZwischenUploadsInMs);

                //Arbeitsschleife
                while (!neustarten)
                {
                    structure.Zustand.WertAusSpeicherLesen();

                    if (ausfuehrungsmodell.AktuellerZustandModulAktivieren())
                    {
                        structure.Lesen();
                        ausfuehrungsmodell.Folgezustand();
                        structure.Zustand.WertInSpeicherSchreiben();
                    }
                    
                    Helfer.SleepForMicroseconds(konfiguration.PauseArbeitsschleifeUs);
                }

                zyklischeAusfuehrungUpload.Dispose();

                neustarten = false;
            }
        }
    }
}
