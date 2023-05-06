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
            public string token;
            public string adresse;
            public string orgId;
            public string pfad;
            public int abbildlaenge;
            public int pause;

            public Konfiguration(string datei) : base(datei) { }
        }
        
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            var konfiguration = new Konfiguration("konfiguration.json");

            var beschreibung = new Modul(konfiguration.Identifikation, "isci.abbild");
            beschreibung.Name = "Abbild Ressource " + konfiguration.Identifikation;
            beschreibung.Beschreibung = "Modul zur Abbilderstellung gegen externe Datenbank";
            beschreibung.Speichern(konfiguration.OrdnerBeschreibungen + "/" + konfiguration.Identifikation + ".json");

            var structure = new Datenstruktur(konfiguration.OrdnerDatenstruktur);
            structure.DatenmodelleEinhängenAusOrdner(konfiguration.OrdnerDatenmodelle);
            structure.Start();

            var influxDBClient = InfluxDBClientFactory.Create(konfiguration.adresse, konfiguration.token);
            influxDBClient.SetLogLevel(InfluxDB.Client.Core.LogLevel.None);

            var writeOptions = WriteOptions
            .CreateNew()
            .BatchSize(50000)
            .FlushInterval(10000)
            .Build();

            var writeApi = influxDBClient.GetWriteApi(writeOptions);
            var orgname = (await influxDBClient.GetOrganizationsApi().FindOrganizationByIdAsync(konfiguration.orgId)).Name;
            
            var bucketApi = influxDBClient.GetBucketsApi();
            var bucketApiOrg = (await bucketApi.FindBucketsByOrgNameAsync(orgname));

            var buckets = new List<string>();
            foreach (var bucket in bucketApiOrg)
            {
                buckets.Add(bucket.Name);
            }

            if (!buckets.Contains(konfiguration.Anwendung))
            {
                bucketApi.CreateBucketAsync(new Bucket(name:konfiguration.Anwendung, orgID:konfiguration.orgId,
                retentionRules:new List<BucketRetentionRules>(){
                    new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0, 3600)
                })).Wait();
            }

            var abbild = new List<string>();

            /*if (!System.IO.File.Exists("written")) {
                System.IO.File.Create("written").Close();
            }

            var written = System.IO.File.ReadAllLines("written").ToList<string>();*/

            while(true)
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

                structure.Lesen();
                var dt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

                foreach (var feld in structure.dateneinträge)
                {
                    var l = $"{feld.Key},ressource={konfiguration.Ressource} value={feld.Value.Serialisieren()} {dt}";
                    abbild.Add(l);
                }

                if (abbild.Count > konfiguration.abbildlaenge)
                {
                    writeApi.WriteRecords(abbild, bucket:konfiguration.Anwendung, org:konfiguration.orgId, precision:WritePrecision.Ms);
                    abbild.Clear();
                }

                System.Threading.Thread.Sleep(konfiguration.pause);
            }
        }
    }
}
