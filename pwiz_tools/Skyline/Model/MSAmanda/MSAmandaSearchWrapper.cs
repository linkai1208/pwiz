﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using MSAmanda.Core;
using MSAmanda.Utils;
using MSAmanda.InOutput;
using MSAmanda.InOutput.Output;
using pwiz.ProteowizardWrapper;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Util;
using MSAmandaEnzyme = MSAmanda.Utils.Enzyme;
using AmandaSettings = MSAmanda.InOutput.Settings;
using Thread = System.Threading.Thread;

namespace pwiz.Skyline.Model.MSAmanda
{
    public class MSAmandaSearchWrapper : AbstractDdaSearchEngine
    {
        //public string[] FastaFiles { get; set; } = new string[0];
        //public string[] SpectraFiles { get; set; } =  new string[0];
        internal Settings Settings { get; private set; } = new Settings();
        private static MSHelper helper = new MSHelper();
        private SettingsFile AvailableSettings;
        private OutputMzid mzID;
        private MSAmandaSearch SearchEngine;
        private OutputParameters _outputParameters;
        private MSAmandaSpectrumParser amandaInputParser;

        public override event NotificationEventHandler SearchProgessChanged;



        private const string UNIMOD_FILENAME = "Unimod.xml";
        private const string ENZYME_FILENAME = "enzymes.xml";
        private const string INSTRUMENTS_FILENAME = "Instruments.xml";
        #region todo add as additional settings
        private const string AMANDA_DB_DIRECTORY = "C:\\ProgramData\\MSAmanda2.0\\DB";
        private const string AMANDA_SCRATCH_DIRECTORY = "C:\\ProgramData\\MSAmanda2.0\\Scratch";
        private const double CORE_USE_PERCENTAGE = 100;
        private const int MAX_NUMBER_PROTEINS = 10000;
        private const int MAX_NUMBER_SPECTRA = 1000;
        private const string AmandaResults = "AmandaResults";
        private const string AmandaDB = "AmandaDB";
        private const string AmandaMap = "AmandaMap";
        private readonly string _baseDir = "C:\\ProgramData\\MSAmanda2.0";
        #endregion

        public MSAmandaSearchWrapper(){
            if (!helper.IsInitialized())
            {
                helper.InitLogWriter(_baseDir);
                
            }
            helper.SearchProgressChanged += Helper_SearchProgressChanged;
            var folderForMappings = Path.Combine(_baseDir, AmandaMap);
            // creates dir if not existing
            Directory.CreateDirectory(folderForMappings);
            mzID = new OutputMzid(folderForMappings);
            AvailableSettings = new SettingsFile(helper, Settings, mzID);
            AvailableSettings.AllEnzymes = new List<MSAmandaEnzyme>();
            AvailableSettings.AllModifications = new List<Modification>();
            AvailableSettings.ParseEnzymeFile(ENZYME_FILENAME, "", AvailableSettings.AllEnzymes);
            AvailableSettings.ParseUnimodFile(UNIMOD_FILENAME, AvailableSettings.AllModifications);
            AvailableSettings.ParseOboFiles();
            AvailableSettings.ReadInstrumentsFile(INSTRUMENTS_FILENAME);
            
        }

        private void Helper_SearchProgressChanged(string message)
        {
            SearchProgessChanged?.Invoke(this, new MessageEventArgs(){Message = message});
        }

        public override void SetEnzyme(pwiz.Skyline.Model.DocSettings.Enzyme enzyme, int maxMissedCleavages)
        {
            MSAmandaEnzyme e = AvailableSettings.AllEnzymes.Find(enz => enz.Name.ToUpper() == enzyme.Name.ToUpper());
            if (e != null)
            {
                Settings.MyEnzyme = e;
                Settings.MissedCleavages = maxMissedCleavages;
            }
            else
            {
                //todo construct new enzyme
            }
        }

       

        public void SetTolerance(double tol, string unit, bool isMS1)
        {
            MassUnit massUnit = MassUnit.DA;
            switch (unit.ToUpper())
            {
                case "PPM":
                    massUnit = MassUnit.PPM;
                    break;
                case "MMU":
                    massUnit = MassUnit.MMU;
                    break;
                default:
                    massUnit = MassUnit.DA;
                    break;
            }
            Tolerance tolerance = new Tolerance(tol, massUnit);
            if (isMS1)
                Settings.Ms1Tolerance = tolerance;
            else
                Settings.Ms2Tolerance = tolerance;
        }

        public override string[] FragmentIons
        {
            get { return Settings.ChemicalData.Instruments.Keys.ToArray(); }
        }
        public override string EngineName { get { return "MS Amanda"; } }
        public override Bitmap SearchEngineLogo
        {
            get { return Properties.Resources.MSAmandaLogo; }
        }

        public override void SetPrecursorMassTolerance(double mass, string unit)
        {
            SetTolerance(mass, unit, true);
        }

        public override void SetFragmentIonMassTolerance(double mass, string unit)
        {
            SetTolerance(mass, unit, false);
        }

        public override void SetFragmentIons(string ions)
        {
            if (Settings.ChemicalData.Instruments.ContainsKey(ions))
            {
                Settings.ChemicalData.CurrentInstrumentSetting = Settings.ChemicalData.Instruments[ions];
            }
        }

        private MSAmandaEngine amandaSearchEngine;

        private List<FastaDBFile> GetFastaFileList()
        {
            List<FastaDBFile> files = new List<FastaDBFile>();
            foreach (string f in FastaFileNames)
            {
                AFastaFile file = new AFastaFile();
                file.FullPath = f;
                file.NeatName = Path.GetFileNameWithoutExtension(f);
                files.Add(new FastaDBFile() { fastaTarged = file});
            }

            return files;
        }


        private void InitializeEngine(CancellationTokenSource token, string spectrumFileName)
        {
            _outputParameters = new OutputParameters();
            _outputParameters.DBFile = FastaFileNames[0];
            //_outputParameters.IsMzOutput = true;
            //2 == mzid
            _outputParameters.SetOutputFileFormat(2);
            _outputParameters.IsPercolatorOutput = true;
            //_outputParameters.SpectraFiles = SpectrumFileNames.ToList();
            _outputParameters.SpectraFiles = new List<string>() { spectrumFileName};
            Settings.GenerateDecoyDb = true;
            Settings.ConsideredCharges.Clear();
            Settings.ConsideredCharges.Add(2);
            Settings.ConsideredCharges.Add(3);
            Settings.ChemicalData.UseMonoisotopicMass = true;
            Settings.ReportBothBestHitsForTD = false;
            mzID.Settings = Settings;
            SearchEngine = new MSAmandaSearch(helper, _baseDir, _outputParameters, Settings, token);
            //OutputMzid mzid = new OutputMzid();
            SearchEngine.InitializeOutputMZ(mzID);

            //List<FastaDBFile> fastaFiles = GetFastaFileList();
            //var folderForMappings = Path.Combine(_baseDir, AmandaMap);
            //// creates dir if not existing
            //Directory.CreateDirectory(folderForMappings);
            //mzID = new OutputMzid(folderForMappings);

            //SearchEngine = new MSAmandaEngine(AMANDA_DB_DIRECTORY, AMANDA_SCRATCH_DIRECTORY, CORE_USE_PERCENTAGE, MAX_NUMBER_PROTEINS, fastaFiles, );
            //amandaSearchEngine = new MSAmandaEngine(AMANDA_DB_DIRECTORY, AMANDA_SCRATCH_DIRECTORY, CORE_USE_PERCENTAGE, MAX_NUMBER_PROTEINS, 
            //    GetFastaFileList(), Settings.ChemicalData.CurrentInstrumentSetting,Settings.MyEnzyme, true, Settings.Ms1Tolerance, 
            //    Settings.Ms2Tolerance, Settings.SelectedModifications, Settings.MissedCleavages);

            ////// set searchagainstdecoy
            ////_searchAgainstDecoy = _settings.GenerateDecoyDb;
            //// save fasta with numbers in own folder                        
            //GenerateMappings();
            //// Generate Directories for SearchEngine
            //GenerateDirectoriesForSearchEngine();
        }

        //private void GenerateMappings()
        //{
        //    var folderForMappings = Path.Combine(_baseDir, AmandaMap);
        //    //if (!_settings.FolderName.ToUpper().Equals("DEFAULT"))
        //    //{
        //    //    folderForMappings = Path.Combine(_settings.FolderName, MsAmanda2Foldername, AmandaMap);
        //    //}

        //    // creates dir if not existing
        //    Directory.CreateDirectory(folderForMappings);
        //    // for normal search
        //    // get correct filenames            
        //    _mappedFastaFile = _helper.GetFastaMapFileName(folderForMappings, _outputParameters.DBFile);
        //    // mappingfile, internal ids to geneidentifiers
        //    _mappingFile = _helper.GetFastaMappingFileName(folderForMappings, _outputParameters.DBFile);
        //    // check if files exist and are generated before last changedate from dbFileName
        //    _helper.CheckForMappingFile(_outputParameters.DBFile, _mappedFastaFile, _mappingFile, _outputMzid);

        //    // for decoy search
        //    // IDs with REV_
        //    // filenames with _map_decoy   
        //    if (_settings.PerformDecoySearch)
        //    {
        //        _decoyMappedFastaFile = _helper.GetFastaMapFileName(folderForMappings, _outputParameters.DBFile, true);
        //        // mappingfile, internal ids to geneidentifiers
        //        _decoyMappingFile = _helper.GetFastaMappingFileName(folderForMappings, _outputParameters.DBFile, true);
        //        // check if files exist and are generated before last changedate from dbFileName
        //        _helper.CheckForMappingFile(_outputParameters.DBFile, _decoyMappedFastaFile, _decoyMappingFile,
        //            _outputMzid, true);
        //    }
        //}

        private bool success = true;
        public override bool Run(CancellationTokenSource tokenSource)
        {
            try
            {
                
                List<Spectrum> spectra = new List<Spectrum>();
                foreach (string rawFileName in SpectrumFileNames)
                {
                    InitializeEngine(tokenSource, rawFileName);
                    if (tokenSource.Token.IsCancellationRequested)
                    {
                        
                        tokenSource.Token.ThrowIfCancellationRequested();
                    }

                    amandaInputParser = new MSAmandaSpectrumParser(rawFileName, Settings.ConsideredCharges, true);
                    SearchEngine.SetInputParser(amandaInputParser);

                    SearchEngine.PerformSearch(_outputParameters.DBFile);
                    //Dictionary<int, SpectrumMatchesCollection> result = amandaSearchEngine.PerformSearch(spectra);
                    //WriteResults(result);


                }

            }
            catch (Exception ex)
            {
        SearchEngine.Dispose();
                helper.WriteMessage("Search is being canceled", true);
                success = false;
            }

            if (tokenSource.IsCancellationRequested)
                success = false;
            
            return success;
           

        }

        

        

        public override void SaveModifications(Dictionary<StaticMod, bool> fixedAndVariableModifs)
        {
            Settings.SelectedModifications.Clear();
            foreach (var item in fixedAndVariableModifs)
            {
                string name = item.Key.Name.Split(' ')[0];
                var elemsFromUnimod = AvailableSettings.AllModifications.FindAll(m => m.Title == name);
                if (elemsFromUnimod.Count> 0)
                {
                    foreach (char aa in item.Key.AminoAcids)
                    {
                        var elem = elemsFromUnimod.Find(m => m.AA == aa);
                        if (elem != null)
                        {
                            Modification modClone = new Modification(elem);
                            modClone.Fixed = item.Value;
                            Settings.SelectedModifications.Add(modClone);
                        }
                        else
                        {
                            //Settings.SelectedModifications.Add(GenerateNewModification(item.Key, item.Value));
                        }
                    }
                }
                else
                {
                    //Settings.SelectedModifications.Add(GenerateNewModification(item.Key, item.Value));
                }
            }
        }

        private Modification GenerateNewModification(StaticMod mod, bool isFixed)
        {
            //todo
            return null;
        }
    }
}
