﻿using FFXIV_TexTools.Resources;
using FFXIV_TexTools.Views.Controls;
using HelixToolkit.Wpf;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using MahApps.Metro.IconPacks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using xivModdingFramework.Cache;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Helpers;
using xivModdingFramework.Items.Categories;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Mods;
using xivModdingFramework.Textures.Enums;
using xivModdingFramework.Textures.FileTypes;
using xivModdingFramework.Variants.FileTypes;

namespace FFXIV_TexTools.Views.Item
{
    public partial class ItemViewControl : UserControl, INotifyPropertyChanged
    {
        /*  This class is the primary container for the core TexTools "Item" based view system.
         *  It primarily serves a wrapper for the various File Wrapper tabs.
         *  With the majority of its logic serving to parse an IItem into its constituent files,
         *  and pass those file paths to the correct places,
         *  as well as wrapping those file paths into dropdown selections in some of the tabs.
        */

        // TODO: Make this prettier.
        private static SolidColorBrush SelectedBrush = new SolidColorBrush(new Color() { A = 255, R = 0, G = 0, B = 0 });

        private static SolidColorBrush UnselectedBrush = new SolidColorBrush(new Color() { A = 0, R = 0, G = 0, B = 0 });

        private string _ItemNameText;
        public string ItemNameText
        {
            get
            {
                return _ItemNameText;
            }
            set
            {
                _ItemNameText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemNameText)));
            }
        }

        public bool RefreshEnabled
        {
            get
            {
                return Item != null;
            }
        }


        private List<FileWrapperControl> FileControls = new List<FileWrapperControl>();
        #region Basic IPropertyNotify Properties Pattern

        public event PropertyChangedEventHandler PropertyChanged;

        public static ItemViewControl MainItemView { get; private set; }

        private IItem _Item;
        public IItem Item
        {
            get => _Item;
            private set
            {
                _Item = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Item)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RefreshEnabled)));
            }
        }

        private ObservableCollection<KeyValuePair<string, string>> _Models { get; set; }
        public ObservableCollection<KeyValuePair<string, string>> Models
        {
            get => _Models;
            set
            {
                _Models = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Models)));
            }
        }

        private ObservableCollection<KeyValuePair<string, string>> _Materials { get; set; }
        public ObservableCollection<KeyValuePair<string, string>> Materials
        {
            get => _Materials;
            set
            {
                _Materials = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Materials)));
            }
        }

        private ObservableCollection<KeyValuePair<string, string>> _Textures { get; set; }
        public ObservableCollection<KeyValuePair<string, string>> Textures
        {
            get => _Textures;
            set
            {
                _Textures = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Textures)));
            }
        }

        private bool _ModelsEnabled;
        public bool ModelsEnabled
        {
            get => _ModelsEnabled;
            set
            {
                _ModelsEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModelsEnabled)));
            }
        }

        private bool _MaterialsEnabled;
        public bool MaterialsEnabled
        {
            get => _MaterialsEnabled;
            set
            {
                _MaterialsEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaterialsEnabled)));
            }
        }

        private bool _TexturesEnabled;
        public bool TexturesEnabled
        {
            get => _TexturesEnabled;
            set
            {
                _TexturesEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TexturesEnabled)));
            }
        }

        private bool _MetadataEnabled;
        public bool MetadataEnabled
        {
            get => _MetadataEnabled;
            set
            {
                _MetadataEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MetadataEnabled)));
            }
        }

        private bool _ItemInfoEnabled;
        public bool ItemInfoEnabled
        {
            get => _ItemInfoEnabled;
            set
            {
                _ItemInfoEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemInfoEnabled)));
            }
        }

        private bool _AddMaterialEnabled;
        public bool AddMaterialEnabled
        {
            get => _AddMaterialEnabled;
            set
            {
                _AddMaterialEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddMaterialEnabled)));
            }
        }

        #endregion


        private bool _TargetFileSet {  get
            {
                return _TargetFile.ModelKey != null;
            } 
        }

        // Special indicator requesting not to change the visible panel.
        private bool _TargetFileLocked { get
            {
                return _TargetFile.ModelKey == "!";
            } 
        }

        private (string ModelKey, string MaterialKey, string TextureKey) _TargetFile = (null, null, null);

        /// <summary>
        /// Internal dictionary structure representing all of the files available for this item.
        /// [Model] => [Referenced Materials] => [Referenced Textures]
        /// 
        /// If a layer is missing, empty string is used as the only key.
        /// </summary>
        private Dictionary<string, Dictionary<string, HashSet<string>>> Files;

        private XivDependencyRoot Root;

        public ItemViewControl()
        {
            DataContext = this;
            InitializeComponent();

            TextureWrapper.SetControlType(typeof(TextureFileControl));
            MaterialWrapper.SetControlType(typeof(MaterialFileControl));
            ModelWrapper.SetControlType(typeof(ModelFileControl));
            MetadataWrapper.SetControlType(typeof(MetadataFileControl));

            FileControls.Add(TextureWrapper);
            FileControls.Add(MaterialWrapper);
            FileControls.Add(ModelWrapper);
            FileControls.Add(MetadataWrapper);

            MetadataWrapper.FileControl.FileSaved += OnMetadataSaved;

            ExtraButtonsRow.Height = new GridLength(0);
            ItemNameText = "No Item Selected";

            // Go ahead and show the model panel.
            // The viewport there can take a second to initialize,
            // So it helps a little to get it visible to kick that stuff off earlier.
            ShowPanel(ModelWrapper);

            TextureWrapper.FileControl.FileDeleted += FileControl_FileDeleted;
            MaterialWrapper.FileControl.FileDeleted += FileControl_FileDeleted;
            ModelWrapper.FileControl.FileDeleted += FileControl_FileDeleted;
            MetadataWrapper.FileControl.FileDeleted += Metadata_FileDeleted;

            ModTransaction.FileChangedOnCommit += ModTransaction_FileChanged;
            if (MainWindow.UserTransaction != null)
            {
                MainWindow.UserTransaction.FileChanged += ModTransaction_FileChanged;
            }
            MainWindow.UserTransactionStarted += MainWindow_UserTransactionStarted;

            _DebouncedRebuildComboBoxes = ViewHelpers.Debounce<IItem>(DispatchRebuildComboBoxes);
        }
        private void MainWindow_UserTransactionStarted()
        {
            if (MainWindow.UserTransaction != null)
            {
                MainWindow.UserTransaction.FileChanged += ModTransaction_FileChanged;
            }
        }

        private Action<IItem> _DebouncedRebuildComboBoxes;

        private async void DispatchRebuildComboBoxes(IItem item)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    var tx = MainWindow.DefaultTransaction;
                    _TargetFile = GetFileKeys(_VisiblePanel.FilePath);
                    await RebuildComboBoxes(tx);
                });
            }
            catch(Exception ex)
            {
                // No-Op
                Trace.WriteLine(ex);
            }
        }

        private async void ModTransaction_FileChanged(string changedFile, long newOffset)
        {
            if (Files == null) return;

            if (string.IsNullOrWhiteSpace(changedFile))
            {
                return;
            }

            try
            {

                // This is where we can listen for files getting modified.
                var fRoot = XivCache.GetFilePathRoot(changedFile);
                if(Root == fRoot)
                {
                    // File is contained in our root...
                    var keys = GetFileKeys(changedFile);
                    if (keys.ModelKey == "!")
                    {
                        // But is not already listed in our file structure.
                        // This means we need to reload the item.
                        _DebouncedRebuildComboBoxes(Item);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                // No Op
                Trace.WriteLine(ex);
            }
        }
        private async void Metadata_FileDeleted(string internalPath)
        {
            try
            {
                // Deleted metadata basically required reloading the item due to the bredth of possible changes.
                await Dispatcher.InvokeAsync(async () =>
                {
                    await SetItem(Item);
                });
            }
            catch (Exception ex)
            {
                //No Op
                Trace.WriteLine(ex);
            }
        }

        private async void FileControl_FileDeleted(string internalPath)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    await SafeRemoveFile(internalPath);
                });
            }
            catch(Exception ex)
            {
                //No Op
                Trace.WriteLine(ex);
            }
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            var wind = Window.GetWindow(this);
            if (wind == MainWindow.GetMainWindow())
            {
                MainItemView = this;
            }

            wind.PreviewKeyDown += Wind_PreviewKeyDown;
            wind.PreviewMouseRightButtonDown += Wind_PreviewMouseRightButtonDown;
        }


        private async void OnMetadataSaved(FileViewControl sender, bool success)
        {
            try
            {
                // Always reload the item on Metadata reload, to be safe.
                await SetItem(Item, Item.GetRoot().Info.GetRootFile());
            }
            catch
            {
                // No-Op, just safety catch.
            }
        }

        /// <summary>
        /// Simple shortcut accessor and null checking for ActiveView.SetItem(item)
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public static async Task StaticSetItem(IItem item)
        {
            if(MainItemView == null)
            {
                return;
            }
            await MainItemView.SetItem(item);
        }

        /// <summary>
        /// Primary setter for other areas in TexTools to assign an item to this view.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<bool> SetItem(IItem item, string targetFile = null)
        {
            var res = await HandleUnsaveConfirmation(null, null);
            if (!res)
            {
                return false;
            }

            if(Item == item && targetFile == null)
            {
                targetFile = _VisiblePanel.FilePath;
            }

            ProgressDialogController lockController = null;
            if (this == MainItemView)
            {
                await MainWindow.GetMainWindow().LockUi("Loading Item", "Please wait...", this);
            }
            else
            {
                var wind = Window.GetWindow(this) as MetroWindow;
                if (wind != null)
                {
                    lockController = await wind.ShowProgressAsync("Loading Item", "Please wait...");
                }
            }

            try
            {
                Item = item;
                ModelsEnabled = false;
                MaterialsEnabled = false;
                TexturesEnabled = false;
                MetadataEnabled = false;
                ItemInfoEnabled = false;
                AddMaterialEnabled = false;
                Models = new ObservableCollection<KeyValuePair<string, string>>();
                Materials = new ObservableCollection<KeyValuePair<string, string>>();
                Textures = new ObservableCollection<KeyValuePair<string, string>>();
                Files = new Dictionary<string, Dictionary<string, HashSet<string>>>();
                ItemNameText = "Loading Item...";

                ResetWatermarks();

                if (Item == null)
                {
                    await TextureWrapper.ClearFile();
                    await MaterialWrapper.ClearFile();
                    await ModelWrapper.ClearFile();
                    await MetadataWrapper.ClearFile();

                    ItemNameText = "No Item Selected";
                    ShowPanel(null);
                    return true;
                }

                ItemInfoEnabled = true;
                Root = Item.GetRoot();

                if (Root == null)
                {
                    await MaterialWrapper.ClearFile();
                    await ModelWrapper.ClearFile();
                    await MetadataWrapper.ClearFile();

                    // Texture only item.  Not implemented yet.
                    TexturesEnabled = true;
                    ShowPanel(TextureWrapper);
                    ItemNameText = ":(";
                    throw new NotImplementedException();
                    return false;
                }

                // We can add materials to anything with a root.
                AddMaterialEnabled = true;

                var tx = MainWindow.DefaultTransaction;

                await SetItemName(tx);
                _TargetFile = GetFileKeys(targetFile);

                // Load metadata view manually since it's not handled by the above functions.
                var success = await MetadataWrapper.LoadInternalFile(Root.Info.GetRootFile(), Item, null, false);
                if (success)
                {
                    MetadataEnabled = true;
                }

                await RebuildComboBoxes(tx);



                return true;
            }
            catch(Exception ex) 
            {
                this.ShowError("Item Load Error", "An error occurred while loading the item:\n\n" + ex.Message);
                return false;
            }
            finally
            {

                if (this == MainItemView)
                {
                    await MainWindow.GetMainWindow().UnlockUi(this);
                } else if(lockController != null)
                {
                    await lockController.CloseAsync();
                }
            }
        }

        private async Task RebuildComboBoxes(ModTransaction tx) {

            // Populate the Files structure.
            await Task.Run(async () =>
            {
                // These need to be in sequence, so they can't be paralell'd
                await GetModels(tx);
                await GetMaterials(tx);
                await GetTextures(tx);
            });

            // Assign the first combo box, which will kick off the children boxes in turn.
            AddModels(Files.Keys.ToList());
        }

        private async Task SetItemName(ModTransaction tx)
        {
            if(Item == null)
            {
                ItemNameText = "No Item Selected";
                return;
            } else if(Root == null)
            {
                ItemNameText = Item.Name;
                return;
            }

            var variantString = "";
            var asIm = Item as IItemModel;
            if (Imc.UsesImc(Root) && asIm != null && asIm.ModelInfo != null && asIm.ModelInfo.ImcSubsetID >= 0)
            {
                variantString += " - Variant " + asIm.ModelInfo.ImcSubsetID;

                var mSetId = await Imc.GetMaterialSetId(asIm, false, tx);
                if (mSetId >= 0)
                {
                    variantString = " - Material Set " + mSetId;
                }
            }

            ItemNameText = Root.Info.GetBaseFileName() + variantString + " : " + Item.Name;
        }

        private void ResetWatermarks()
        {

            TextBoxHelper.SetWatermark(ModelComboBox, UIStrings.Model);
            TextBoxHelper.SetWatermark(MaterialComboBox, UIStrings.Material);
            TextBoxHelper.SetWatermark(TextureComboBox, UIStrings.Texture);
        }
        private void AssignModelWatermark()
        {
            var ct = Models.Count;
            if (Models.Count == 0 || Models[0].Value == "")
            {
                ct = 0;
            }

            var markText = UIStrings.Model;
            if (ct == 0 || ct > 1)
            {
                markText = UIStrings.Models;
            }

            markText += " (" + ct + ")";
            TextBoxHelper.SetWatermark(ModelComboBox, markText);
        }
        private void AssignMaterialWatermark()
        {
            string markText = UIStrings.Material;
            var ct = Materials.Count;
            if (Materials.Count == 0 || Materials[0].Value == "")
            {
                ct = 0;
            }
            if (ct == 0 || ct > 1)
            {
                markText = UIStrings.Materials;
            }

            markText += " (" + ct + ")";
            TextBoxHelper.SetWatermark(MaterialComboBox, markText);
        }
        private void AssignTexturewatermark()
        {

            var ct = Textures.Count;
            if (Textures.Count == 0 || Textures[0].Value == "")
            {
                ct = 0;
            }

            string markText = UIStrings.Texture;
            if (ct == 0 || ct > 1)
            {
                markText = UIStrings.Textures;
            }

            markText += " (" + ct + ")";
            TextBoxHelper.SetWatermark(TextureComboBox, markText);
        }

        /// <summary>
        /// Populates the top level strcture of the Files dictionary.
        /// </summary>
        private async Task GetModels(ModTransaction tx)
        {
            Files = new Dictionary<string, Dictionary<string, HashSet<string>>>();

            if (Root == null)
            {
                Files.Add("", new Dictionary<string, HashSet<string>>());
                return;
            }

            var models = await Root.GetModelFiles(tx);
            foreach (var m in models)
            {
                Files.Add(m, new Dictionary<string, HashSet<string>>());
            }

            if(Files.Count == 0)
            {
                Files.Add("", new Dictionary<string, HashSet<string>>());
            }
            return;
        }


        /// <summary>
        /// Populates the second level structure of the Files dictionary
        /// </summary>
        /// <returns></returns>
        private async Task GetMaterials(ModTransaction tx)
        {
            if(Root == null)
            {
                Files.Add("", new Dictionary<string, HashSet<string>>());
                return;
            }

            var asIm = Item as IItemModel;
            var materialSet = -1;
            if(asIm != null)
            {
                materialSet = await Imc.GetMaterialSetId(asIm, false, tx);
            }

            if (Root.Info.PrimaryType == XivItemType.human && Root.Info.SecondaryType == XivItemType.body)
            {
                // Exceptions class.
                var materials = await Root.GetMaterialFiles(-1, tx, false);
                var key = Files.First().Key;
                foreach (var mat in materials)
                {
                    if (!Files[key].ContainsKey(mat))
                    {
                        Files[key].Add(mat, new HashSet<string>());
                    }
                }
            }
            else
            {
                // Resolve by referenced materials.
                foreach (var file in Files)
                {
                    var model = file.Key;
                    var materials = await Root.GetVariantShiftedMaterials(model, materialSet, tx);
                    foreach (var mat in materials)
                    {
                        if (!Files[model].ContainsKey(mat))
                        {
                            Files[model].Add(mat, new HashSet<string>());
                        }
                    }
                }
            }


            var orphanMaterials = await Root.GetOrphanMaterials(materialSet, tx);
            if (Root.Info.SecondaryType != null)
            {
                // If there is a secondary ID, just snap these onto the first entry, because there's only one model (or 0).
                var entry = Files.First().Value;
                foreach (var orph in orphanMaterials)
                {
                    if (!entry.ContainsKey(orph)) {
                        entry.Add(orph, new HashSet<string>());
                    }
                }
            }
            else
            {
                foreach (var orph in orphanMaterials)
                {
                    // This goes to the matching fake-secondary entry, if there is one.
                    // Ex. on Equipment, the race is a fake secondary value.
                    var secondary = IOUtil.GetSecondaryIdFromFileName(orph);
                    var match = Files.FirstOrDefault(x => IOUtil.GetSecondaryIdFromFileName(x.Key) == secondary);
                    if(match.Key != null && match.Value != null)
                    {
                        if (!match.Value.ContainsKey(orph))
                        {
                            match.Value.Add(orph, new HashSet<string>());
                        }
                    }
                    else
                    {
                        var entry = Files.First().Value;
                        entry.Add(orph, new HashSet<string>());
                    }
                }
            }

            // Ensure we have at least a blank entry.
            foreach (var file in Files)
            {
                if (file.Value.Count == 0)
                {
                    file.Value.Add("", new HashSet<string>());
                }
            }
        }

        private async Task<(string model, List<string> materials)> GetMaterialsTask(string model, XivDependencyRoot root, ModTransaction tx)
        {
            var materials = await root.GetMaterialFiles(-1, tx, false);
            return (model, materials);
        }

        private async Task GetTextures(ModTransaction tx)
        {

            // Anything with materials is easy. 
            foreach(var mdlKv in Files)
            {
                foreach(var mtrlKv in Files[mdlKv.Key])
                {
                    var mtrl = mtrlKv.Key;
                    if(mtrlKv.Key == "")
                    {
                        break;
                    }

                    var textures = await Mtrl.GetTexturePathsFromMtrlPath(mtrl, false, false, tx);
                    foreach(var tex in textures)
                    {
                        mtrlKv.Value.Add(tex);
                    }
                }
            }

            if(Files.Count == 0)
            {
                // TODO: Add resoultion for texture-only items.
                Files.Add("", new Dictionary<string, HashSet<string>>());
            }
        }

        /// <summary>
        /// Adds the given paths to the Models combo box.
        /// </summary>
        /// <param name="models"></param>
        private void AddModels(IEnumerable<string> models)
        {
            Models = new ObservableCollection<KeyValuePair<string, string>>();
            foreach (var model in models)
            {
                if (model == "")
                {
                    Models.Add(new KeyValuePair<string, string>("--", ""));
                    break;
                }

                var niceName = Path.GetFileNameWithoutExtension(model);
                var race = IOUtil.GetRaceFromPath(model);
                var baseName = Root.Info.GetBaseFileName();

                if (race != XivRace.All_Races)
                {
                    niceName = race.GetDisplayName();
                }
                else if (model.StartsWith(baseName) && model != baseName)
                {
                    niceName = model.Substring(Root.Info.GetBaseFileName().Length);
                }

                Models.Add(new KeyValuePair<string, string>(niceName, model));
            }

            if (Models.Count == 0)
            {
                Models.Add(new KeyValuePair<string, string>("--", ""));
            }

            AssignModelWatermark();
            ModelsEnabled = true;

            if (_TargetFileSet && Models.Any(x => x.Value == _TargetFile.ModelKey))
            {
                ModelComboBox.SelectedValue = _TargetFile.ModelKey;
            } else
            {
                // Default behavior
                ModelComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Adds the given paths to the Materials combo box.
        /// </summary>
        /// <param name="models"></param>
        private void AddMaterials(IEnumerable<string> materials)
        {
            Materials = new ObservableCollection<KeyValuePair<string, string>>();
            foreach (var material in materials)
            {
                if(material == "")
                {
                    Materials.Add(new KeyValuePair<string, string>("--", ""));
                    break;
                }
                var niceName = Path.GetFileNameWithoutExtension(material);
                var race = IOUtil.GetRaceFromPath(material);
                var baseName = Root.Info.GetBaseFileName();

                Materials.Add(new KeyValuePair<string, string>(niceName, material));
            }

            if (Materials.Count == 0)
            {
                Materials.Add(new KeyValuePair<string, string>("--", ""));
            }

            AssignMaterialWatermark();
            MaterialsEnabled = true;

            if (_TargetFile.MaterialKey != null && Materials.Any(x => x.Value == _TargetFile.MaterialKey))
            {
                MaterialComboBox.SelectedValue = _TargetFile.MaterialKey;
            }
            else
            {
                // Default behavior
                MaterialComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Adds the given paths to the Textures combo box.
        /// </summary>
        /// <param name="textures"></param>
        private async Task AddTextures(IEnumerable<string> textures)
        {
            var mtrlPath = (string)MaterialComboBox.SelectedValue;
            XivMtrl mtrl = null;
            if (!string.IsNullOrEmpty(mtrlPath))
            {
                var tx = MainWindow.DefaultTransaction;
                if(await tx.FileExists(mtrlPath))
                {
                    mtrl = await Mtrl.GetXivMtrl(mtrlPath, false, tx);
                }
            }

            Textures = new ObservableCollection<KeyValuePair<string, string>>();
            foreach (var texture in textures)
            {
                if (texture == "")
                {
                    Textures.Add(new KeyValuePair<string, string>("--", ""));
                    break;
                }

                var niceName = Path.GetFileNameWithoutExtension(texture);

                if (mtrl != null && mtrl.Textures.Any(x => x.Dx11Path == texture))
                {
                    niceName = mtrl.ResolveFullUsage(mtrl.Textures.First(x => x.Dx11Path == texture)).ToString() + " - " +  niceName;
                }
                else if (mtrl != null && mtrl.Textures.Any(x => x.Dx9Path == texture))
                {
                    niceName = "DX9 - " + niceName;
                }
                else
                {
                    
                }



                Textures.Add(new KeyValuePair<string, string>(niceName, texture));
            }

            if (Textures.Count == 0)
            {
                Textures.Add(new KeyValuePair<string, string>("--", ""));
            }

            AssignTexturewatermark();
            TexturesEnabled = true;
            if (_TargetFile.TextureKey != null && Textures.Any(x => x.Value == _TargetFile.TextureKey))
            {
                TextureComboBox.SelectedValue = _TargetFile.TextureKey;
            }
            else
            {
                // Default behavior
                TextureComboBox.SelectedIndex = 0;
            }
        }

        // Internal state control flag.
        private bool _CANCELLING_COMBO_BOXES;


        private async Task<bool> HandleUnsaveConfirmation(ComboBox c, SelectionChangedEventArgs e)
        {
            if (_CANCELLING_COMBO_BOXES)
            {
                // Mid-reset.
                return false;
            }

            if (e != null)
            {
                if (e.RemovedItems.Count == 0)
                {
                    // Something broke, unfortunate.
                    return true;
                }
            }

            var res = true;
            bool modelPrompt = false;
            bool materialPrompt = false;
            bool texPrompt = false;

            if (ModelWrapper.UnsavedChanges && ((c == ModelComboBox) || c == null))
            {
                res = ModelWrapper.FileControl.ConfirmDiscardChanges(ModelWrapper.FilePath);
                modelPrompt = true;
            }
            if (res && MaterialWrapper.UnsavedChanges && (c == MaterialComboBox || c == ModelComboBox || c == null))
            {
                res = MaterialWrapper.FileControl.ConfirmDiscardChanges(MaterialWrapper.FilePath);
                materialPrompt = true;
            }
            if (res && TextureWrapper.UnsavedChanges && c == null)
            {
                res= TextureWrapper.FileControl.ConfirmDiscardChanges(TextureWrapper.FilePath);
                texPrompt = true;
            }

            // If user rejected any unsave confirmations
            if (!res)
            {

                // Restore selected combo box back to its previous state.
                if (c != null && e != null)
                {
                    _CANCELLING_COMBO_BOXES = true;
                    c.SelectedItem = e.RemovedItems[0];
                    _CANCELLING_COMBO_BOXES = false;
                }

                return false;
            }

            // Clear flags.
            if (res && modelPrompt)
            {
                ModelWrapper.UnsavedChanges = false;
            }

            if (res && materialPrompt)
            {
                MaterialWrapper.UnsavedChanges = false;
            }

            if (res && texPrompt)
            {
                TextureWrapper.UnsavedChanges = false;
            }

            return true;
        }


        private (string ModelKey, string MaterialKey, string TexKey) GetFileKeys(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return (null, null, null);
            }

            if(Files == null)
            {
                return (null, null, null);
            }

            foreach (var mkv in Files)
            {
                if (mkv.Key == file)
                {
                    return (file, null, null);
                }
                foreach (var mtkv in mkv.Value)
                {
                    if (mtkv.Key == file)
                    {
                        return (mkv.Key, file, null);
                    }

                    foreach (var tex in mtkv.Value)
                    {
                        if (tex == file)
                        {
                            return (mkv.Key, mtkv.Key, file);
                        }
                    }
                }
            }
            
            // File not in our tree, don't force visibility.
            return ("!", null, null);
        }

        private bool RemoveFile(string file)
        {
            foreach (var mkv in Files)
            {
                if (mkv.Key == file)
                {
                    Files.Remove(file);
                    return true;
                }
                foreach(var mtkv in mkv.Value)
                {
                    if(mtkv.Key == file)
                    {
                        Files[mkv.Key].Remove(file);
                        return true;
                    }

                    foreach(var tex in mtkv.Value)
                    {
                        if(tex == file)
                        {
                            Files[mkv.Key][mtkv.Key].Remove(tex);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Tests if a file exists, safely removing it from the combo boxes as non-disruptively as possible if it doesn't.
        /// </summary>
        /// <param name="shiftUp"></param>
        /// <returns></returns>
        private async Task<bool> SafeRemoveFile(string fileRemoved)
        {
            if (string.IsNullOrWhiteSpace(fileRemoved))
            {
                return false;
            }

            var tx = MainWindow.DefaultTransaction;
            if(await tx.FileExists(fileRemoved))
            {
                // File still exists, leave it in.
                return false;
            }


            if (!RemoveFile(fileRemoved))
            {
                return false;
            }

            string file = null;
            if (!string.IsNullOrWhiteSpace(fileRemoved))
            {
                var ext = Path.GetExtension(fileRemoved);
                if (TextureWrapper.FileControl.GetValidFileExtensions().Keys.Contains(ext))
                {
                    file = Textures.First().Value;
                    file = file == fileRemoved ? Materials.First().Value : file;
                } else if (MaterialWrapper.FileControl.GetValidFileExtensions().Keys.Contains(ext))
                {
                    file = Materials.First().Value;
                    file = file == fileRemoved ? Models.First().Value : file;
                } else if(ModelWrapper.FileControl.GetValidFileExtensions().Keys.Contains(ext))
                {
                    file = Models.First().Value;
                    file = file == fileRemoved ? null : file;
                } else
                {
                    file = null;
                }
            }

            _TargetFile = GetFileKeys(file);
            AddModels(Files.Keys.ToList());
            return true;
        }

        private async Task SafeAddFile((string ModelKey, string MaterialKey, string TextureKey) keys)
        {
            if(string.IsNullOrWhiteSpace(keys.ModelKey))
            {
                return;
            }

            bool anyChanges = false;

            // Add to the base files dictionary.
            if (!Files.ContainsKey(keys.ModelKey))
            {
                Files.Add(keys.ModelKey, new Dictionary<string, HashSet<string>>());
                anyChanges = true;
            }

            if (keys.MaterialKey != null && !Files[keys.ModelKey].ContainsKey(keys.MaterialKey))
            {
                Files[keys.ModelKey].Add(keys.MaterialKey, new HashSet<string>());
                anyChanges = true;
            }

            if (keys.TextureKey!= null && !Files[keys.ModelKey][keys.MaterialKey].Contains(keys.TextureKey))
            {
                Files[keys.ModelKey][keys.MaterialKey].Add(keys.TextureKey);
                anyChanges = true;
            }

            if(!anyChanges)
            {
                return;
            }

            _TargetFile = keys;

            // Rebuild the list.
            AddModels(Files.Keys.ToList());
        }

        private async void Model_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {

                if (ModelComboBox.SelectedValue == null)
                {
                    return;
                }

                if (!await HandleUnsaveConfirmation(ModelComboBox, e))
                {
                    return;
                }

                var currentModel = (string)ModelComboBox.SelectedValue;

                if (!string.IsNullOrEmpty(currentModel))
                {
                    if(await SafeRemoveFile(currentModel))
                    {
                        return;
                    }
                }

                if (currentModel == null || !Files.ContainsKey(currentModel))
                {
                    AddMaterials(new List<string>());
                    return;
                }

                var mats = Files[currentModel].Keys.ToList();
                var success = await ModelWrapper.LoadInternalFile(currentModel, Item, null, false);

                if(!_TargetFileLocked && 
                    _TargetFile.ModelKey != null
                    && _TargetFile.MaterialKey == null
                    && _TargetFile.TextureKey == null)
                {
                    ShowPanel(ModelWrapper);
                }

                AddMaterials(mats);
            }
            catch
            {
                // No-Op.
            }
        }

        private async void Material_Changed(object sender, SelectionChangedEventArgs e)
        {

            try
            {
                if (MaterialComboBox.SelectedValue == null)
                {
                    return;
                }


                if (!await HandleUnsaveConfirmation(MaterialComboBox, e))
                {
                    return;
                }

                var currentModel = (string)ModelComboBox.SelectedValue;
                if (currentModel == null || !Files.ContainsKey(currentModel))
                {
                    await AddTextures(new List<string>());
                    return;
                }
                var currentMaterial = (string)MaterialComboBox.SelectedValue;

                if (!string.IsNullOrEmpty(currentMaterial))
                {
                    if (await SafeRemoveFile(currentMaterial))
                    {
                        return;
                    }
                }

                if (currentMaterial == null || !Files[currentModel].ContainsKey(currentMaterial))
                {
                    await AddTextures(new List<string>());
                    return;
                }

                var texs = Files[currentModel][currentMaterial];

                var success = await MaterialWrapper.LoadInternalFile(currentMaterial, Item, null, false);

                if (!_TargetFileLocked 
                    && _TargetFile.MaterialKey != null
                    && _TargetFile.TextureKey == null)
                {
                    ShowPanel(MaterialWrapper);
                }

                await AddTextures(texs);
            }
            catch
            {
                // No-Op.
            }

        }

        private async void Texture_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (TextureComboBox.SelectedValue == null)
                {
                    return;
                }

                if (!await HandleUnsaveConfirmation(TextureComboBox, e))
                {
                    return;
                }


                var tex = (string)TextureComboBox.SelectedValue;

                if (!string.IsNullOrEmpty(tex))
                {
                    if (await SafeRemoveFile(tex))
                    {
                        return;
                    }
                }

                var success = await TextureWrapper.LoadInternalFile(tex, Item, null, false);

                if (!_TargetFileLocked && _TargetFile.TextureKey != null)
                {
                    ShowPanel(TextureWrapper);
                }
            }
            catch
            {
                // No-Op.  Should be handled elsewhere, this is just super safety.
            }

            // All done here.
            _TargetFile = (null, null, null);
        }

        private void ItemInfo_Click(object sender, RoutedEventArgs e)
        {

        }

        private FileWrapperControl _VisiblePanel;
        private void ShowPanel(FileWrapperControl control)
        {
            _VisiblePanel = control;

            ModelBorder.BorderBrush = UnselectedBrush;
            MaterialBorder.BorderBrush = UnselectedBrush;
            TextureBorder.BorderBrush = UnselectedBrush;
            //ModelBorder.Margin = new Thickness(5);
            //MaterialBorder.Margin = new Thickness(5);
            //TextureBorder.Margin = new Thickness(5);

            foreach (var panel in FileControls)
            {
                panel.Visibility = panel == control ? Visibility.Visible : Visibility.Collapsed;
            }

            if(control == ModelWrapper && ModelWrapper.FileControl.HasFile)
            {
                //ModelBorder.Margin = new Thickness(2);
                ModelBorder.BorderBrush = SelectedBrush;
            } else if(control == MaterialWrapper && MaterialWrapper.FileControl.HasFile)
            {
                //MaterialBorder.Margin = new Thickness(2);
                MaterialBorder.BorderBrush = SelectedBrush;
            } else if (control == TextureWrapper && TextureWrapper.FileControl.HasFile)
            {
                //TextureBorder.Margin = new Thickness(2);
                TextureBorder.BorderBrush = SelectedBrush;
            }
        }

        private void ShowTexture_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(TextureWrapper);
        }

        private void ShowMaterial_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(MaterialWrapper);
        }

        private void ShowModel_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(ModelWrapper);
        }

        private void ShowExtraButtons_Click(object sender, RoutedEventArgs e)
        {
            if (ExtraButtonsRow.Height.Value == 0)
            {
                ExtraButtonsRow.Height =  new GridLength(40);
                //var icon = new FontAwesomeExtension(PackIconFontAwesomeKind.SortUpSolid);
                //ShowExtraButtonsButton.Content = icon;
                //ExtraButtonsIcon.Kind = PackIconFontAwesomeKind.SortUpSolid;
            } else
            {
                ExtraButtonsRow.Height = new GridLength(0);
                //var icon = new FontAwesomeExtension(PackIconFontAwesomeKind.SortDownSolid);
                //ShowExtraButtonsButton.Content = icon;
                //ExtraButtonsIcon.Kind = PackIconFontAwesomeKind.SortDownSolid;
            }
        }

        private void ShowMetadata_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(MetadataWrapper);
        }

        private async void AddMaterial_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var matControl = (MaterialFileControl)MaterialWrapper.FileControl;
                var currentModel = (string) ModelComboBox.SelectedValue;
                var result = await CreateMaterialDialog.ShowCreateMaterialDialogSimple(currentModel, matControl.Material, Item, Window.GetWindow(this));
                if(result == null)
                {
                    return;
                }

                // Load the new data at the new path.
                var data = Mtrl.XivMtrlToUncompressedMtrl(result);
                await MaterialWrapper.LoadInternalFile(result.MTRLPath, Item, data, false);
                ShowPanel(MaterialWrapper);

                // Hook the next material save to reload the item, so that we can get our new material in our combo boxes.
                matControl.FileSaved += MatControl_FileSaved;
                matControl.FileLoaded += MatControl_FileLoaded;
            }
            catch(Exception ex)
            {
                Trace.Write(ex);
            }
        }

        private void MatControl_FileLoaded(FileViewControl sender, bool success)
        {
            // User switched off the new material without saving it.
            MaterialWrapper.FileControl.FileLoaded -= MatControl_FileLoaded;
            MaterialWrapper.FileControl.FileSaved -= MatControl_FileSaved;
        }

        private async void MatControl_FileSaved(FileViewControl sender, bool success)
        {
            try
            {
                var matControl = (MaterialFileControl)MaterialWrapper.FileControl;
                matControl.FileLoaded -= MatControl_FileLoaded;
                matControl.FileSaved -= MatControl_FileSaved;

                await SetItem(Item, matControl.Material.MTRLPath);
            } catch(Exception ex)
            {
                // No op, just safety catch.
                Trace.WriteLine(ex);
            }
        }

        private bool _DROPDOWN_OPEN;
        private bool _DROPDOWN_CANCEL;
        private void Combobox_DropdownOpened(object sender, EventArgs e)
        {
            _DROPDOWN_OPEN = true;
            _DROPDOWN_CANCEL = false;
        }

        private void ModelComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_DROPDOWN_CANCEL)
            {
                _DROPDOWN_CANCEL = false;
                return;
            }

            _DROPDOWN_CANCEL = false;
            ShowPanel(ModelWrapper);
        }

        private void MaterialComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_DROPDOWN_CANCEL)
            {
                _DROPDOWN_CANCEL = false;
                return;
            }

            _DROPDOWN_CANCEL = false;
            ShowPanel(MaterialWrapper);
        }

        private void TextureComboBox_DropDownClosed(object sender, EventArgs e)
        {
            if (_DROPDOWN_CANCEL)
            {
                _DROPDOWN_CANCEL = false;
                return;
            }

            _DROPDOWN_CANCEL = false;
            ShowPanel(TextureWrapper);
        }
        private void Wind_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_DROPDOWN_OPEN)
            {
                var controlSource = e.OriginalSource as Control;
                if (controlSource != null && controlSource.Parent == null)
                {
                    // Clicked on the content of the dropdown, which is just null-parent floating text boxes.
                    // This doesn't close the popup window.
                    return;
                }
                _DROPDOWN_CANCEL = true;
            }
        }
        private void Wind_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape)
            {
                if (_DROPDOWN_OPEN)
                {
                    _DROPDOWN_CANCEL = true;
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var success = await SetItem(Item);
            }
            catch
            {
                // No op
            }
        }

        private async void PopOut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var success = await SimpleItemViewWindow.ShowItem(Item, Window.GetWindow(this));
            }
            catch
            {
                // No op
            }
        }
    }
}
