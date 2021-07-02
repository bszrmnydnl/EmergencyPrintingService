using System;
using System.Configuration;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Timers;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.IO;
using System.Data.SqlClient;
using Aspose.Pdf;
using System.Reflection;

namespace EmergencyPrintingService
{
    public enum ServiceState
    {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus
    {
        public int dwServiceType;
        public ServiceState dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    };

    public partial class EmergencyPrintingService : ServiceBase
    {

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus serviceStatus);
        SqlConnection connection;
        private int eventId = 1;
        private int lastPrint = 0;
        private string printerPath = "";
        private string pdfLocation = "";
        private string sqlServer = "79.139.58.214";
        private string sqlDatabase = "porta_mylan";
        private string sqlUser = "porta_mylan";
        private string sqlPassword = "pxvtpZ9Ct493jkLf";
        private string sqlTable = "pitoporta";
        private string ignoreTimeAfterPrint = "600";

        public EmergencyPrintingService()
        {
            InitializeComponent();
            //Logger létrehozása
            eventLog1 = new System.Diagnostics.EventLog();
            try
            {
                if (!EventLog.SourceExists("EmergencyPrintingService"))
                {
                    System.Diagnostics.EventLog.CreateEventSource("EmergencyPrintingService", "");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Nem sikerült a logger létrehozása. " + e.Message);
            }
            eventLog1.Source = "EmergencyPrintingService";
            eventLog1.Log = "";
        }

        protected override void OnStart(string[] args)
        {
            // A service állapot "Indítás folyamatban"-ra állítása
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 10000; // 10 másodperc
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
            eventLog1.WriteEntry("Emergency Printing Service is starting...");

            // Config fájl beolvasása
            ReadAllSettings();

            // Számláló létrehozása a visszatérő ellenőrzésekhez
            Timer timer = new Timer();
            timer.Interval = 10000; // 10 másodperc
            timer.Elapsed += new ElapsedEventHandler(this.OnTimer);
            timer.Start();

            // Adatbáziskapcsolat 
            connection = new SqlConnection("Server=" + sqlServer + ";Database=" + sqlDatabase + ";User Id=" + sqlUser + ";Password=" + sqlPassword + ";");
            connection.Open();

            // A service állapot "Fut"-ra állítása
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        private void OnTimer(object sender, ElapsedEventArgs e)
        {
            if (GetTime() - lastPrint > int.Parse(ignoreTimeAfterPrint))
            {
                // Ez a metódus fut le 10 másodpercenként.
                eventLog1.WriteEntry("Checking database for emergency flag...", EventLogEntryType.Information, eventId++);
                String queryString = "SELECT status FROM " + sqlTable + " WHERE id=1";
                SqlCommand command = new SqlCommand(queryString, connection);
                String status = "";
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        status = String.Format("{0}", reader["status"]);
                    }
                }

                switch (StatusCheck(status))
                {
                    case 0:
                        {
                            status = "a";
                            eventLog1.WriteEntry("Flag found: " + status + " ; Ignoring...", EventLogEntryType.Warning, eventId++);
                            break;
                        }
                    case 1:
                        {
                            status = "A";
                            eventLog1.WriteEntry("Flag found: " + status + " ; Printing list of people... ", EventLogEntryType.Warning, eventId++);

                            int mode = 1;

                            if (mode == 0)
                            {

                                // Névlista PDF letöltése és nyomtatásra küldése
                                using (var client = new System.Net.WebClient())
                                {
                                    // Névlista letöltése
                                    client.DownloadFile(pdfLocation, "toPrint.pdf");
                                    FileStream pdfStream = File.OpenRead("toPrint.pdf");
                                    try
                                    {
                                        lastPrint = GetTime();
                                        // Névlista küldése nyomtatásra
                                        _ = PrintPdf(pdfStream, printerPath, "");
                                        eventLog1.WriteEntry("Document successfully sent to printer. Going idle for " + Int32.Parse(ignoreTimeAfterPrint) / 60 + " minutes.", EventLogEntryType.Information, eventId++);
                                    }
                                    catch (InvalidOperationException ioe)
                                    {
                                        eventLog1.WriteEntry(ioe.Message, EventLogEntryType.Error, eventId++);
                                    }
                                }

                            }
                            else if (mode == 1)
                            {

                                // Névlista létrehozása és nyomtatásra küldése
                                Document document = new Document();
                                Page page = document.Pages.Add();
                                page.Paragraphs.Add(new Aspose.Pdf.Text.TextFragment("Bent tartózkodó személyek listája:"));
                                page.Paragraphs.Add(new Aspose.Pdf.Text.TextFragment(DateTime.Now.ToString()));
                                Table table = new Table();
                                table.Border = new BorderInfo(BorderSide.All, .5f, Color.FromRgb(System.Drawing.Color.Black));
                                table.DefaultCellBorder = new BorderInfo(BorderSide.All, .5f, Color.FromRgb(System.Drawing.Color.Black));

                                String listQueryString = "SELECT teljes_nev FROM szemely sz JOIN szemely_mozgas szm ON szm.szemely_mozgas_id = sz.szemely_mozgas_fk WHERE  sz.statusz_fk = 13 AND aktiv_fl = 1 ORDER  BY teljes_nev ASC";
                                SqlCommand listCommand = new SqlCommand(listQueryString, connection);
                                using (SqlDataReader listReader = listCommand.ExecuteReader())
                                {
                                    int row_count = 1;
                                    while (listReader.Read())
                                    {
                                        Row row = table.Rows.Add();
                                        row.Cells.Add("" + row_count);
                                        row.Cells.Add(String.Format("{0}", listReader["teljes_nev"]));

                                        row_count++;
                                    }
                                }

                                document.Pages[1].Paragraphs.Add(table);
                                document.Save("toPrint.pdf");

                                FileStream pdfStream1 = File.OpenRead("toPrint.pdf");
                                try
                                {
                                    lastPrint = GetTime();
                                    // Névlista küldése nyomtatásra
                                    _ = PrintPdf(pdfStream1, printerPath, "");
                                    eventLog1.WriteEntry("Document successfully sent to printer. Going idle for " + Int32.Parse(ignoreTimeAfterPrint) / 60 + " minutes.", EventLogEntryType.Information, eventId++);
                                }
                                catch (InvalidOperationException ioe)
                                {
                                    eventLog1.WriteEntry(ioe.Message, EventLogEntryType.Error, eventId++);
                                }

                            }

                            break;
                        }
                    case 2:
                        {
                            // status változó nem tartalmaz sem "a"-t, sem "A"-t
                            eventLog1.WriteEntry("Status couldn't be confirmed. Status='" + status + "' Skipping...", EventLogEntryType.Error, eventId++);
                            break;
                        }
                }

            }
        }

        protected override void OnStop()
        {
            // A service állapot "Leállítás folyamatban"-ra állítása
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;

            // Adatbáziskapcsolat megszüntetése
            connection.Close();

            // A service állapot "Leállt"-ra állítása
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
            eventLog1.WriteEntry("Emergency Printing Service is stopping...");
        }

        public int StatusCheck(string status)
        {
            if (status.Contains("a"))
            {
                return 0;
            }
            else if (status.Contains("A"))
            {
                return 1;
            }
            else
            {
                return 2;
            }
        }

        public void ReadAllSettings()
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                if (appSettings.Count == 0)
                {
                    eventLog1.WriteEntry("Config file is empty.", EventLogEntryType.Information, eventId++);
                }
                else
                {
                    foreach (var key in appSettings.AllKeys)
                    {
                        switch (key)
                        {
                            case "SERVER": sqlServer = appSettings[key]; break;
                            case "DATABASE": sqlDatabase = appSettings[key]; break;
                            case "USER": sqlUser = appSettings[key]; break;
                            case "PASSWORD": sqlPassword = appSettings[key]; break;
                            case "TABLE": sqlTable = appSettings[key]; break;
                            case "PRINTER PATH": printerPath = appSettings[key]; break;
                            case "PDF LOCATION": pdfLocation = appSettings[key]; break;
                            case "IGNORETIME": ignoreTimeAfterPrint = appSettings[key]; break;
                        }
                    }
                }
            }
            catch (ConfigurationErrorsException)
            {
                eventLog1.WriteEntry("Error reading config file.", EventLogEntryType.Error, eventId++);
            }
            finally
            {
                eventLog1.WriteEntry("Config file read successfully.", EventLogEntryType.Information, eventId++);
            }
        }

        public async Task PrintPdf(Stream pdfStream, string printerPath, string pageRange)
        {
            // A nyomtató servicenek való küldéshez szükséges http asynctask
            HttpClient httpClient = new HttpClient();
            var formContent = new MultipartFormDataContent();
            var printerPathContent = new StringContent(printerPath);
            var pageRangeContent = new StringContent(pageRange ?? string.Empty);
            var pdfFileContent = new StreamContent(pdfStream);

            formContent.Add(printerPathContent, "printerPath");
            formContent.Add(pdfFileContent, "pdfFile", "file.pdf");
            if (!string.IsNullOrWhiteSpace(pageRange))
                formContent.Add(pageRangeContent, "pageRange");

            var endpoint = new Uri("http://localhost:7000/print/from-pdf");
            HttpResponseMessage result = await httpClient.PostAsync(endpoint, formContent);
            if (!result.IsSuccessStatusCode)
            {
                string content = await result.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to send PDF for PrintService. StatusCode = {result.StatusCode}, Response = {content}");
            }
        }

        public int GetTime()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return (int)t.TotalSeconds;
        }
    }
}