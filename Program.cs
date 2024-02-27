using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using isci;
using isci.Allgemein;
using isci.Beschreibung;
using isci.Daten;

namespace isci.abbild
{
    class Program
    {
        public class Konfiguration : Parameter
        {
            [fromArgs, fromEnv]
            public string influxDbToken;
            [fromArgs, fromEnv]
            public string influxDbAdresse;
            [fromArgs, fromEnv]
            public string influxDbOrganisationId;
            [fromArgs, fromEnv]
            public int minimaleAnzahlFuerDbUpload;
            [fromArgs, fromEnv]
            public int pauseZwischenUploadsInMs;
            public Konfiguration(string[] args) : base(args) { }
        }

        static List<string> abbild;

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
                writeApi.WriteRecords(abbild, bucket:konfiguration.Anwendung, org:konfiguration.influxDbOrganisationId, precision:WritePrecision.Ms);
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

            start:

            abbild = new List<string>();

            var structure = new Datenstruktur(konfiguration);
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

            influxDBClient = InfluxDBClientFactory.Create(konfiguration.influxDbAdresse, konfiguration.influxDbToken);
            influxDBClient.SetLogLevel(InfluxDB.Client.Core.LogLevel.None);

            var writeOptions = WriteOptions
            .CreateNew()
            .BatchSize(50000)
            .FlushInterval(10000)
            .Build();

            writeApi = influxDBClient.GetWriteApi(writeOptions);
            var orgname = (await influxDBClient.GetOrganizationsApi().FindOrganizationByIdAsync(konfiguration.influxDbOrganisationId)).Name;
            
            var bucketApi = influxDBClient.GetBucketsApi();
            var bucketApiOrg = await bucketApi.FindBucketsByOrgNameAsync(orgname);

            var buckets = new List<string>();
            foreach (var bucket in bucketApiOrg)
            {
                buckets.Add(bucket.Name);
            }

            if (!buckets.Contains(konfiguration.Anwendung))
            {
                bucketApi.CreateBucketAsync(new Bucket(name:konfiguration.Anwendung, orgID:konfiguration.influxDbOrganisationId,
                retentionRules:new List<BucketRetentionRules>(){
                    new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0, 60*60*24*365)
                })).Wait();
            }

            var zyklischeAusfuehrungUpload = new System.Threading.Timer(AbbildErfassen, null, 0, konfiguration.pauseZwischenUploadsInMs);

            //Arbeitsschleife
            while (!neustarten)
            {
                if (ausfuehrungsmodell.AktuellerZustandModulAktivieren())
                {
                    structure.Lesen();
                }
                System.Threading.Thread.Sleep(1);
            }

            zyklischeAusfuehrungUpload.Dispose();

            neustarten = false;
            goto start;
        }
    }
}
