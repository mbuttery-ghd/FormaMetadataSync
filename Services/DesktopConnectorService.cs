using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LiteDB;

namespace AccC3DMetadata.Services
{
    /// <summary>
    /// Result of resolving a locally-synced Desktop Connector file to its ACC identifiers.
    /// </summary>
    public sealed class DesktopConnectorFileInfo
    {
        /// <summary>ACC hub ID, or <c>null</c> if the local cache record did not include one.</summary>
        public string HubId { get; init; }

        /// <summary>ACC project ID (includes the <c>"b."</c> prefix, matching the Data Management API).</summary>
        public string ProjectId { get; init; }

        /// <summary>URN of the ACC folder that contains the file — directly usable as a Data Management folder ID.</summary>
        public string FolderUrn { get; init; }

        /// <summary>The tip version URN of the file at the time the cache was last updated.</summary>
        public string VersionUrn { get; init; }

        /// <summary>The file's display name, as recorded in the cache.</summary>
        public string Name { get; init; }
    }

    /// <summary>
    /// Resolves ACC hub/project/folder identifiers for a locally-synced Desktop Connector file
    /// by reading the connector's own LiteDB property cache directly, avoiding any network round-trip.
    /// </summary>
    /// <remarks>
    /// Desktop Connector maintains one <c>*.properties.db</c> LiteDB file per remote folder under
    /// <c>%LOCALAPPDATA%\Autodesk\Desktop Connector\Data</c>. Each database's own file name is a
    /// (possibly-suffixed) base64 encoding of that folder's ACC URN, and its
    /// <c>FileSystemProperties</c> collection holds one BSON document per file in that folder with
    /// a doubly-JSON-encoded <c>MetaData.Data</c> payload containing <c>Name</c>, <c>VersionUrn</c>,
    /// <c>HubId</c>, and <c>ProjectId</c>.
    /// </remarks>
    internal static class DesktopConnectorService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Autodesk",
            "Desktop Connector",
            "Data"
        );

        /// <summary>
        /// Scans the local Desktop Connector cache for a file matching <paramref name="localDwgPath"/>'s
        /// file name and returns its ACC identifiers, or <c>null</c> if no cache entry is found
        /// (e.g. the connector has not yet indexed the folder, or is not installed).
        /// </summary>
        public static DesktopConnectorFileInfo TryResolveDrawingLocation(string localDwgPath)
        {
            if (string.IsNullOrEmpty(localDwgPath) || !Directory.Exists(DataFolder))
                return null;

            string targetName = Path.GetFileName(localDwgPath);

            IEnumerable<string> dbFiles;
            try
            {
                dbFiles = Directory.EnumerateFiles(
                    DataFolder,
                    "*.properties.db",
                    SearchOption.AllDirectories
                );
            }
            catch (IOException)
            {
                return null;
            }

            foreach (string dbPath in dbFiles)
            {
                var match = TryFindInPropertiesDb(dbPath, targetName);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static DesktopConnectorFileInfo TryFindInPropertiesDb(
            string dbPath,
            string targetName
        )
        {
            try
            {
                using var db = new LiteDatabase(
                    $"Filename={dbPath};Connection=shared;ReadOnly=true"
                );
                if (!db.GetCollectionNames().Contains("FileSystemProperties"))
                    return null;

                string folderUrn = DecodeUrn(Path.GetFileName(dbPath)) ?? dbPath;
                var collection = db.GetCollection("FileSystemProperties", BsonAutoId.ObjectId);

                foreach (BsonDocument document in collection.FindAll())
                {
                    var data = ExtractData(document);
                    if (data == null)
                        continue;

                    string name = GetString(data.Value, "Name");
                    string versionUrn = GetString(data.Value, "VersionUrn");
                    if (name == null || versionUrn == null)
                        continue;
                    if (!string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return new DesktopConnectorFileInfo
                    {
                        HubId = GetString(data.Value, "HubId"),
                        ProjectId = GetString(data.Value, "ProjectId"),
                        FolderUrn = folderUrn,
                        VersionUrn = versionUrn,
                        Name = name,
                    };
                }
            }
            catch (Exception)
            {
                // Skip any DB we can't open (locked, corrupt, unrelated schema, etc.) and keep scanning.
            }
            return null;
        }

        /// <summary>Unwraps the doubly-JSON-encoded <c>MetaData.Data</c> payload of a cache document.</summary>
        private static JsonElement? ExtractData(BsonDocument document)
        {
            var root = ParseJson(document.ToString());
            string metaDataJson = root.HasValue ? GetString(root.Value, "MetaData") : null;
            var metaData = ParseJson(metaDataJson);
            string dataJson = metaData.HasValue ? GetString(metaData.Value, "Data") : null;
            return ParseJson(dataJson);
        }

        private static JsonElement? ParseJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            try
            {
                return JsonDocument.Parse(text).RootElement;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        /// <summary>
        /// Decodes a Desktop Connector cache file name back into the ACC folder URN it represents.
        /// The connector appends a suffix (e.g. <c>.properties.db</c>) to the base64-encoded URN.
        /// </summary>
        private static string DecodeUrn(string fileName)
        {
            foreach (string suffix in new[] { ".properties.db", ".brcache.db", "-rocks-db" })
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    fileName = fileName[..^suffix.Length];

            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(fileName));
                return decoded.StartsWith("urn:adsk", StringComparison.OrdinalIgnoreCase)
                    ? decoded
                    : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
