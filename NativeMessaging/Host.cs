﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace NativeMessaging
{
    /// <summary>
    /// Abstract class that should be extended to communicate with Chrome
    /// </summary>
    public abstract class Host
    {
        /// <summary>
        /// Name of the Native Messaging Host
        /// </summary>
        public abstract string Hostname { get; }

        private readonly bool SendConfirmationReceipt;
        private readonly string ManifestPath;

        private const string RegKeyBaseLocation = "SOFTWARE\\Google\\Chrome\\NativeMessagingHosts\\";

        /// <summary>
        /// Creates the Host Object
        /// </summary>
        /// <param name="sendConfirmationReceipt"><see langword="true" /> for the host to automatically send message confirmation receipt.</param>
        public Host(bool sendConfirmationReceipt = true)
        {
            SendConfirmationReceipt = sendConfirmationReceipt;
            ManifestPath = Path.Combine(Utils.AssemblyLoadDirectory(), Hostname + "-manifest.json");
        }

        /// <summary>
        /// Starts listening for input.
        /// </summary>
        public void Listen()
        {
            if (!IsRegisteredWithChrome()) throw new NotRegisteredWithChromeException(Hostname);
            JObject data;
            while ((data = Read()) != null)
            {
                Utils.LogMessage("Data Received:" + JsonConvert.SerializeObject(data));
                if (SendConfirmationReceipt) SendMessage(new ResponseConfirmation(data).GetJObject());
                ProcessReceivedMessage(data);
            }
        }

        private JObject Read()
        {
            Utils.LogMessage("Waiting for Data");
            var stdin = Console.OpenStandardInput();

            var lengthBytes = new byte[4];
            stdin.Read(lengthBytes, 0, 4);

            var buffer = new char[BitConverter.ToInt32(lengthBytes, 0)];

            using (var reader = new StreamReader(stdin)) while (reader.Peek() >= 0) reader.Read(buffer, 0, buffer.Length);

            return JsonConvert.DeserializeObject<JObject>(new string(buffer));
        }

        /// <summary>
        /// Sends a message to Chrome, note that the message might not be able to reach Chrome if the stdIn / stdOut aren't properly configured (i.e. Process needs to be started by Chrome)
        /// </summary>
        /// <param name="data">A <see cref="JObject"/> containing the data to be sent.</param>
        public void SendMessage(JObject data)
        {
            Utils.LogMessage("Sending Message:" + JsonConvert.SerializeObject(data));
            var bytes = System.Text.Encoding.UTF8.GetBytes(data.ToString(Formatting.None));
            var stdout = Console.OpenStandardOutput();
            stdout.WriteByte((byte)((bytes.Length >> 0) & 0xFF));
            stdout.WriteByte((byte)((bytes.Length >> 8) & 0xFF));
            stdout.WriteByte((byte)((bytes.Length >> 16) & 0xFF));
            stdout.WriteByte((byte)((bytes.Length >> 24) & 0xFF));
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }

        /// <summary>
        /// Override this method in your extended <see cref="Host"/> to process messages received from Chrome.
        /// </summary>
        /// <param name="data">A <see cref="JObject"/> containing the data received.</param>
        protected abstract void ProcessReceivedMessage(JObject data);

        /// <summary>
        /// Generates the manifest and saves it to the correct location.
        /// </summary>
        /// <param name="description">Short application description to be included in the manifest.</param>
        /// <param name="allowedOrigins">List of extensions that should have access to the native messaging host.<br />Wildcards such as <code>chrome-extension://*/*</code> are not allowed.</param>
        public void GenerateManifest(string description, string[] allowedOrigins)
        {
            Utils.LogMessage("Generating Manifest");
            var manifest = JsonConvert.SerializeObject(new Manifest(Hostname, description, Utils.AssemblyExecuteablePath(), allowedOrigins));
            File.WriteAllText(ManifestPath, manifest);
            Utils.LogMessage("Manifest Generated");
        }
        /// <summary>
        /// Register the application to open with Chrome.
        /// </summary>
        public void Register()
        {
            var regHostnameKeyLocation = RegKeyBaseLocation + Hostname;

            RegistryKey regKey = Registry.CurrentUser.OpenSubKey(regHostnameKeyLocation, true);

            if (regKey == null) regKey = Registry.CurrentUser.CreateSubKey(regHostnameKeyLocation);

            regKey.SetValue("", ManifestPath, RegistryValueKind.String);

            regKey.Close();
            Utils.LogMessage("Registered:" + Hostname);
        }

        /// <summary>
        /// Checks if the host is registered with Chrome
        /// </summary>
        /// <returns><see langword="true"/> if the required information is present in the registry.</returns>
        public bool IsRegisteredWithChrome()
        {
            var regHostnameKeyLocation = RegKeyBaseLocation + Hostname;
            RegistryKey regKey = Registry.CurrentUser.OpenSubKey(regHostnameKeyLocation, true);

            if (regKey != null && regKey.GetValue("").ToString() == ManifestPath) return true;

            return false;
        }

        /// <summary>
        /// De-register the application to open with Chrome.
        /// </summary>
        public void UnRegister()
        {
            var regHostnameKeyLocation = RegKeyBaseLocation + Hostname;

            RegistryKey regKey = Registry.CurrentUser.OpenSubKey(regHostnameKeyLocation, true);
            if (regKey != null) regKey.DeleteSubKey("", true);
            regKey.Close();
            Utils.LogMessage("Unregistered:" + Hostname);
        }
    }
}
