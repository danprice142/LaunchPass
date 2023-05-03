using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Search;
using Windows.UI.Xaml;

namespace RetroPass
{
    public class DataSourceManager
    {
        public enum DataSourceLocation
        {
            None,
            Local,
            Removable
        }

        private readonly string ActiveDataSourceLocationKey = "ActiveDataSourceLocationKey";
        private readonly string ImportFinishedKey = "ImportFinishedKey";

        public bool ImportFinished
        {
            get
            {
                bool val = true;
                if (ApplicationData.Current.LocalSettings.Values[ImportFinishedKey] != null)
                {
                    val = (bool)ApplicationData.Current.LocalSettings.Values[ImportFinishedKey];
                }
                return val;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[ImportFinishedKey] = value;
            }
        }

        public DataSourceLocation ActiveDataSourceLocation
        {
            get
            {
                DataSourceLocation val = DataSourceLocation.None;
                if (ApplicationData.Current.LocalSettings.Values[ActiveDataSourceLocationKey] != null)
                {
                    val = Enum.Parse<DataSourceLocation>(ApplicationData.Current.LocalSettings.Values[ActiveDataSourceLocationKey] as string);
                }
                return val;
            }
            private set
            {
                ApplicationData.Current.LocalSettings.Values[ActiveDataSourceLocationKey] = value.ToString();
            }
        }

        public Action OnImportStarted;
        public Action<float> OnImportUpdateProgress;
        public Action<bool> OnImportFinished;
        public Action OnImportError;

        private StorageFile removableStorageFile = null;
        private StorageFile localStorageFile = null;
        private DataSource dataSource;

        private static CancellationTokenSource tokenSource;

        public DataSourceManager()
        {
            //delete all settings
            //_ = ApplicationData.Current.ClearAsync();
        }

        public async Task ScanDataSource()
        {
            Trace.TraceInformation("DataSourceManager: ScanDataSource");
            localStorageFile = await GetConfigurationFile(DataSourceLocation.Local);
            removableStorageFile = await GetConfigurationFile(DataSourceLocation.Removable);
        }

        private async Task<LaunchPassConfig> GetConfiguration(DataSourceLocation location)
        {
            var file = await GetConfigurationFile(location);
            LaunchPassConfig configuration = null;

            if (file != null)
            {
                string xmlConfig = await FileIO.ReadTextAsync(file);

                using (TextReader reader = new StringReader(xmlConfig))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(LaunchPassConfig));
                    // Call the De-serialize method to restore the object's state.
                    configuration = serializer.Deserialize(reader) as LaunchPassConfig;
                }
            }
            return configuration;
        }

        private async Task<StorageFile> GetConfigurationFile(DataSourceLocation location)
        {
            StorageFile file = null;

            switch (location)
            {
                case DataSourceLocation.None:
                    break;

                case DataSourceLocation.Local:
                    file = await GetConfigFile(ApplicationData.Current.LocalCacheFolder);
                    break;

                case DataSourceLocation.Removable:
                    // Get the logical root folder for all external storage devices.
                    IReadOnlyList<StorageFolder> removableFolders = await KnownFolders.RemovableDevices.GetFoldersAsync();

                    foreach (var folder in removableFolders)
                    {
                        file = await GetConfigFile(folder);

                        if (file != null)
                        {
                            break;
                        }
                    }
                    break;

                default:
                    break;
            }

            return file;
        }

        public async Task<DataSource> GetDataSource(DataSourceLocation location)
        {
            DataSource dataSource = null;

            switch (location)
            {
                case DataSourceLocation.None:
                    break;

                case DataSourceLocation.Local:
                    if (localStorageFile != null)
                    {
                        dataSource = await GetDataSourceFromConfigurationFile(localStorageFile);
                    }
                    break;

                case DataSourceLocation.Removable:
                    if (removableStorageFile != null)
                    {
                        dataSource = await GetDataSourceFromConfigurationFile(removableStorageFile);
                    }
                    break;

                default:
                    break;
            }

            return dataSource;
        }

        public async void ActivateDataSource(DataSourceLocation location)
        {
            ActiveDataSourceLocation = location;
            dataSource = await GetDataSource(ActiveDataSourceLocation);
            ThumbnailCache.Instance.Set(dataSource, ActiveDataSourceLocation);
        }

        public async Task<DataSource> GetActiveDataSource()
        {
            if (dataSource == null)
            {
                await ScanDataSource();
                dataSource = await GetDataSource(ActiveDataSourceLocation);
                ThumbnailCache.Instance.Set(dataSource, ActiveDataSourceLocation);
            }

            return dataSource;
        }

        public bool HasDataSource(DataSourceLocation dataSourceLocation)
        {
            switch (dataSourceLocation)
            {
                case DataSourceLocation.None:
                    return false;

                case DataSourceLocation.Local:
                    return localStorageFile != null;

                case DataSourceLocation.Removable:
                    return removableStorageFile != null;

                default:
                    return false;
            }
        }

        private async Task<DataSource> GetDataSourceFromConfigurationFile(StorageFile xmlConfigFile)
        {
            DataSource dataSource = null;

            if (xmlConfigFile != null)
            {
                Trace.TraceInformation("DataSourceManager: GetDataSourceFromConfigurationFile {0}", xmlConfigFile.Path);
                string xmlConfig = await FileIO.ReadTextAsync(xmlConfigFile);

                using (TextReader reader = new StringReader(xmlConfig))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(LaunchPassConfig));
                    // Call the De-serialize method to restore the object's state.
                    LaunchPassConfig configuration = serializer.Deserialize(reader) as LaunchPassConfig;

                    string rootFolder = Path.Combine(Path.GetDirectoryName(xmlConfigFile.Path), configuration.relativePath);
                    rootFolder = Path.GetFullPath(rootFolder);

                    if (configuration.type == LaunchPassConfig.DataSourceType.LaunchBox)
                    {
                        dataSource = new DataSourceLaunchBox(rootFolder, configuration);
                    }

                    /*if (dataSource != null)
					{
						string rootFolder = Path.Combine(Path.GetDirectoryName(xmlConfigFile.Path), configuration.relativePath);
						dataSource.rootFolder = Path.GetFullPath(rootFolder);
					}*/
                }
            }

            return dataSource;
        }

        private async Task<StorageFile> GetConfigFile(StorageFolder folder)
        {
            StorageFile file = null;

            if (folder != null)
            {
                //check if the location exists
                var item = await folder.TryGetItemAsync("LaunchPass.xml");

                if (item != null)
                {
                    file = item as StorageFile;
                }
            }

            return file;
        }

        public async Task CopyFileAsync(StorageFile source, StorageFolder destinationContainer, bool overwriteIfNewer)
        {
            // Were we already canceled?
            bool copyFile = true;

            if (tokenSource.Token.IsCancellationRequested)
            {
                tokenSource.Token.ThrowIfCancellationRequested();
            }

            //get existing asset
            if (overwriteIfNewer)
            {
                var existingAssetItem = (StorageFile)await destinationContainer.TryGetItemAsync(source.Name);
                if (existingAssetItem != null)
                {
                    BasicProperties existingAssetItemBasicProperties = await existingAssetItem.GetBasicPropertiesAsync();
                    BasicProperties newAssetItemBasicProperties = await source.GetBasicPropertiesAsync();

                    //do not copy file if timestamps are the same
                    if (newAssetItemBasicProperties.DateModified <= existingAssetItemBasicProperties.DateModified)
                    {
                        copyFile = false;
                    }
                }
            }

            if (copyFile == true)
            {
                await source.CopyAsync(destinationContainer, source.Name, NameCollisionOption.ReplaceExisting);
            }
        }

        public async Task CopyFolderAsync(StorageFolder source, StorageFolder destinationContainer, string desiredName = null)
        {
            StorageFolder destinationFolder = null;
            destinationFolder = await destinationContainer.CreateFolderAsync(
                desiredName ?? source.Name, CreationCollisionOption.OpenIfExists);

            foreach (var file in await source.GetFilesAsync())
            {
                await CopyFileAsync(file, destinationFolder, true);
                //await file.CopyAsync(destinationFolder, file.Name, NameCollisionOption.ReplaceExisting);
            }
            foreach (var folder in await source.GetFoldersAsync(CommonFolderQuery.DefaultQuery))
            {
                await CopyFolderAsync(folder, destinationFolder);
            }
        }

        private async Task CopyToLocalFolderAsync()
        {
            // Perform background work here.
            // Don't directly access UI elements from this method.
            DataSource ds = await GetDataSource(DataSourceLocation.Removable);
            await ds.Load();
            string srcPath = ds.rootFolder;
            string destPath = ApplicationData.Current.LocalCacheFolder.Path;

            StorageFolder folderSrc;
            StorageFolder folderDest;
            //find folder
            try
            {
                folderSrc = await StorageUtils.GetFolderFromPathAsync(srcPath);
                folderDest = await StorageUtils.GetFolderFromPathAsync(destPath);
            }
            catch (Exception)
            {
                return;
            }

            List<string> assets = ds.GetAssets();

            //create a launchpass config file
            LaunchPassConfig configRemovable = await GetConfiguration(DataSourceLocation.Removable);
            LaunchPassConfig config = new LaunchPassConfig();
            config.relativePath = "./DataSource";
            config.type = configRemovable.type;
            config.retroarch = ds.LaunchPassConfig.retroarch;

            //save the config file to removable storage
            StorageFile filename = await folderDest.CreateFileAsync("LaunchPass.xml", CreationCollisionOption.ReplaceExisting);
            XmlSerializer x = new XmlSerializer(typeof(LaunchPassConfig));
            using (TextWriter writer = new StringWriter())
            {
                x.Serialize(writer, config);
                await FileIO.WriteTextAsync(filename, writer.ToString());
                localStorageFile = filename;
            }

            var destinationRootFolder = await folderDest.CreateFolderAsync("DataSource", CreationCollisionOption.OpenIfExists);
            var sourceRootFolderPath = ds.rootFolder;
            var sourceRootFolder = await StorageUtils.GetFolderFromPathAsync(sourceRootFolderPath);

            int progress = 0;

            foreach (var asset in assets)
            {
                string dstAssetRelativeDirectoryPath = Path.GetDirectoryName(asset);
                StorageFolder dstAssetFolder = destinationRootFolder;

                IStorageItem assetItem = await sourceRootFolder.TryGetItemAsync(asset);

                if (assetItem == null)
                {
                    continue;
                }

                //create all subdirectories so asset can be copied into them
                if (string.IsNullOrEmpty(dstAssetRelativeDirectoryPath) == false)
                {
                    dstAssetFolder = await destinationRootFolder.CreateFolderAsync(dstAssetRelativeDirectoryPath, CreationCollisionOption.OpenIfExists);
                }

                if (assetItem is StorageFile)
                {
                    await CopyFileAsync((StorageFile)assetItem, dstAssetFolder, true);
                }
                else if (assetItem is StorageFolder)
                {
                    await CopyFolderAsync(assetItem as StorageFolder, dstAssetFolder);
                }

                progress++;

                OnImportUpdateProgress?.Invoke((float)progress / assets.Count * 100.0f);
            }
        }

        public async Task<bool> CopyToLocalFolder()
        {
            tokenSource = new CancellationTokenSource();
            ImportFinished = false;
            OnImportStarted?.Invoke();

            try
            {
                await Task.Run(() => CopyToLocalFolderAsync(), tokenSource.Token);
                ImportFinished = true;
            }
            catch (Exception)
            {
                //copy failed
                ImportFinished = false;
            }
            finally
            {
                tokenSource.Dispose();
                tokenSource = null;
            }

            OnImportFinished?.Invoke(ImportFinished);
            return ImportFinished;
        }

        public void CancelImport()
        {
            tokenSource.Cancel();
        }

        public bool IsImportInProgress()
        {
            return tokenSource != null;
        }

        public async Task DeleteLocalDataSource()
        {
            ImportFinished = true;

            DataSource ds = await GetDataSource(DataSourceLocation.Local);

            if (ds != null)
            {
                await ds.playlistPlayLater.Delete();
            }

            if (ActiveDataSourceLocation == DataSourceLocation.Local)
            {
                ActiveDataSourceLocation = DataSourceLocation.None;
                dataSource = null;
            }

            StorageFolder destPath = ApplicationData.Current.LocalCacheFolder;
            IStorageItem assetItem = await destPath.TryGetItemAsync("DataSource");
            if (assetItem != null)
            {
                await assetItem.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            IStorageItem configItem = await destPath.TryGetItemAsync("LaunchPass.xml");
            if (configItem != null)
            {
                await configItem.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }

            //delete cache
            await ThumbnailCache.Instance.Delete(DataSourceLocation.Local);

            localStorageFile = null;
        }

        public async Task PreparelaunchPassFolder()
        {
            try
            {
                var removableDevices = KnownFolders.RemovableDevices;
                var folders = await removableDevices.GetFoldersAsync();

                StorageFolder launchPassFolderCurrent = null;

                foreach (StorageFolder rootFolder in folders)
                {
                    //FIND LAUNCHBOX FOLDER TO RELATED RETROPASS FOLDER ON THE SAME REMOVABLE DEVICE
                    StorageFolder launchBoxFolder = await rootFolder.TryGetItemAsync("LaunchBox") as StorageFolder;

                    if (launchBoxFolder != null)
                    {
                        //Check storage root directory for LaunchPass.xml config file. (used for setting the data-source location)
                        IStorageItem configItem = await rootFolder.TryGetItemAsync("LaunchPass.xml");
                        if (configItem == null)
                        {
                            // In the event launchpass config file is not found, attempt too create file relative too launchbox data folder
                            LaunchPassConfig config = new LaunchPassConfig();
                            config.relativePath = "./LaunchBox";
                            config.type = LaunchPassConfig.DataSourceType.LaunchBox;

                            //save the file to storage root directory
                            StorageFile filename = await rootFolder.CreateFileAsync("LaunchPass.xml", CreationCollisionOption.ReplaceExisting);
                            XmlSerializer x = new XmlSerializer(typeof(LaunchPassConfig));
                            using (TextWriter writer = new StringWriter())
                            {
                                x.Serialize(writer, config);
                                await FileIO.WriteTextAsync(filename, writer.ToString());
                                localStorageFile = filename;
                            }
                        }

                        // Check removable devices for LaunchPass Folder.
                        launchPassFolderCurrent = await rootFolder.TryGetItemAsync("LaunchPass") as StorageFolder;
                        if (launchPassFolderCurrent != null)
                        // We've Located LaunchPass Folder, check the folder for LaunchPassUserSettings.xml
                        {
                            ((App)Application.Current).LaunchPassRootPath = launchPassFolderCurrent.Path;
                            StorageFile launchPassXMLfile = await launchPassFolderCurrent.GetFileAsync("LaunchPassUserSettings.xml");
                            if (launchPassXMLfile != null)
                            // We've Located LaunchPassUserSettings.xml, attempt to read the file.
                            {
                                string xmlConfig = await FileIO.ReadTextAsync(launchPassXMLfile);

                                using (TextReader reader = new StringReader(xmlConfig))
                                {
                                    XmlSerializer serializer = new XmlSerializer(typeof(LaunchPassThemeSettings));
                                    ((App)Application.Current).CurrentThemeSettings = (LaunchPassThemeSettings)serializer.Deserialize(reader);
                                }
                            }
                            else
                            {
                                //Attempt too create theme settings configuration file.
                                launchPassXMLfile = await launchPassFolderCurrent.CreateFileAsync("LaunchPassUserSettings.xml", CreationCollisionOption.ReplaceExisting);

                                if (launchPassXMLfile != null)
                                // Success, now generate the files contents.
                                {
                                    LaunchPassThemeSettings launchPassDefault = new LaunchPassThemeSettings();
                                    launchPassDefault.Font = "Xbox.ttf";

                                    launchPassDefault.Backgrounds = new Backgrounds()
                                    {
                                        Background = new List<Background>()
                                        {
                                         new Background() { Page = "MainPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "GamePage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "DetailsPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "SearchPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "CustomizePage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "SettingsPage", File = "LaunchPass-LP.mp4" },
                                        }
                                    };

                                    launchPassDefault.BoxArtType = "Box - Front";

                                    XmlSerializer x1 = new XmlSerializer(typeof(LaunchPassThemeSettings));
                                    using (TextWriter writer = new StringWriter())
                                    {
                                        x1.Serialize(writer, launchPassDefault);
                                        await FileIO.WriteTextAsync(launchPassXMLfile, writer.ToString());
                                    }

                                    ((App)Application.Current).CurrentThemeSettings = launchPassDefault;
                                }
                            }

                            //CHECK WHETHER BACKGROUND IMAGES ARE AVAILABLE OR NOT
                            StorageFolder bgFolder = await launchPassFolderCurrent.GetFolderAsync("Backgrounds");
                            if (bgFolder == null)
                                bgFolder = await launchPassFolderCurrent.CreateFolderAsync("Backgrounds");

                            if (((App)Application.Current).CurrentThemeSettings != null && ((App)Application.Current).CurrentThemeSettings.Backgrounds != null && ((App)Application.Current).CurrentThemeSettings.Backgrounds.Background != null)
                            {
                                foreach (var item in ((App)Application.Current).CurrentThemeSettings.Backgrounds.Background)
                                {
                                    if (!await bgFolder.FileExistsAsync(item.File))
                                    {
                                        var bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/LaunchPass-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);
                                        item.File = bgStoreFile.Name;

                                        bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/LaunchPass-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);

                                        bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/LowPolygon-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);

                                        bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Purpz-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);

                                        bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Waves-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);

                                        bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Zoom-LP.mp4"));
                                        await bgStoreFile.CopyAsync(bgFolder, bgStoreFile.Name, NameCollisionOption.ReplaceExisting);
                                    }
                                }
                            }

                            StorageFolder fontFolder = await launchPassFolderCurrent.GetFolderAsync("Fonts");
                            if (fontFolder == null)
                                fontFolder = await launchPassFolderCurrent.CreateFolderAsync("Fonts");

                            StorageFile fontStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/Xbox.ttf"));
                            if (!await fontFolder.FileExistsAsync(fontStoreFile.Name))
                            {
                                await fontStoreFile.CopyAsync(fontFolder, fontStoreFile.Name, NameCollisionOption.ReplaceExisting);
                                ((App)Application.Current).CurrentThemeSettings.Font = "Xbox.ttf";
                            }

                            StorageFolder InstallationFolder = await StorageFolder.GetFolderFromPathAsync(Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Fonts"));

                            foreach (var file in await fontFolder.GetFilesAsync())
                            {
                                await file.CopyAsync(InstallationFolder, file.Name, NameCollisionOption.ReplaceExisting);
                            }

                            XmlSerializer x = new XmlSerializer(typeof(LaunchPassThemeSettings));
                            using (TextWriter writer = new StringWriter())
                            {
                                x.Serialize(writer, ((App)Application.Current).CurrentThemeSettings);
                                await FileIO.WriteTextAsync(launchPassXMLfile, writer.ToString());
                            }
                        }
                        else
                        {
                            launchPassFolderCurrent = await rootFolder.CreateFolderAsync("LaunchPass") as StorageFolder;

                            ((App)Application.Current).LaunchPassRootPath = launchPassFolderCurrent.Path;

                            StorageFolder bgFolder = await launchPassFolderCurrent.CreateFolderAsync("Backgrounds");
                            var fontFolder = await launchPassFolderCurrent.CreateFolderAsync("Fonts");

                            //COPY SAMPLE BACKGROUND AND FONT FILES
                            var bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/FutureLoops-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/LaunchPass-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/LowPolygon-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Purpz-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Waves-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            bgStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Background/Zoom-LP.mp4"));
                            await bgStoreFile.CopyAsync(bgFolder);

                            var fontStoreFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/Xbox.ttf"));
                            await fontStoreFile.CopyAsync(fontFolder);

                            StorageFolder InstallationFolder = await StorageFolder.GetFolderFromPathAsync(Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Fonts"));

                            foreach (var file in await fontFolder.GetFilesAsync())
                            {
                                await file.CopyAsync(InstallationFolder, file.Name, NameCollisionOption.ReplaceExisting);
                            }

                            //Attempt too create theme settings configuration file.
                            StorageFile launchPassXMLfile = await launchPassFolderCurrent.CreateFileAsync("LaunchPassUserSettings.xml", CreationCollisionOption.ReplaceExisting);

                            if (launchPassXMLfile != null)
                            // Success, now generate the files contents.
                            {
                                LaunchPassThemeSettings launchPassDefault = new LaunchPassThemeSettings();
                                launchPassDefault.Font = "Xbox.ttf";

                                launchPassDefault.Backgrounds = new Backgrounds()
                                {
                                    Background = new List<Background>()
                                    {
                                         new Background() { Page = "MainPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "GamePage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "DetailsPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "SearchPage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "CustomizePage", File = "LaunchPass-LP.mp4" },
                                         new Background() { Page = "SettingsPage", File = "LaunchPass-LP.mp4" },
                                    }
                                };

                                launchPassDefault.BoxArtType = "Box - Front";

                                XmlSerializer x = new XmlSerializer(typeof(LaunchPassThemeSettings));
                                using (TextWriter writer = new StringWriter())
                                {
                                    x.Serialize(writer, launchPassDefault);
                                    await FileIO.WriteTextAsync(launchPassXMLfile, writer.ToString());
                                }

                                ((App)Application.Current).CurrentThemeSettings = launchPassDefault;
                            }
                        }

                        break;
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}