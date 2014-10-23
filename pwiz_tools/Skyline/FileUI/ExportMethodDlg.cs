﻿/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NHibernate;
using pwiz.Common.Controls;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Alerts;
using pwiz.Skyline.Controls;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.DocSettings.Extensions;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using pwiz.Skyline.Util.Extensions;

namespace pwiz.Skyline.FileUI
{
    public sealed partial class ExportMethodDlg : FormEx, IMultipleViewProvider
    {
        public static string TRANS_PER_SAMPLE_INJ_TXT { get { return Resources.ExportMethodDlg_TRANS_PER_SAMPLE_INJ_TXT; } }
        public static string CONCUR_TRANS_TXT { get { return Resources.ExportMethodDlg_CONCUR_TRANS_TXT; } }
        public static string PREC_PER_SAMPLE_INJ_TXT { get { return Resources.ExportMethodDlg_PREC_PER_SAMPLE_INJ_TXT; } }
        public static string CONCUR_PREC_TXT { get { return Resources.ExportMethodDlg_CONCUR_PREC_TXT; } }
        public static string RUN_DURATION_TXT { get { return Resources.ExportMethodDlg_RUN_DURATION_TXT; } }
        public static string DWELL_TIME_TXT { get { return Resources.ExportMethodDlg_DWELL_TIME_TXT; } }

        public static string SCHED_NOT_SUPPORTED_ERR_TXT { get { return Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Sched_Not_Supported_Err_Text; } }

        private readonly SrmDocument _document;
        private readonly ExportFileType _fileType;
        private string _instrumentType;

        private readonly ExportDlgProperties _exportProperties;

        public ExportMethodDlg(SrmDocument document, ExportFileType fileType)
        {
            InitializeComponent();

            _exportProperties = new ExportDlgProperties(this);

            _document = document;
            _fileType = fileType;

            string[] listTypes;
            if (_fileType == ExportFileType.Method)
                listTypes = ExportInstrumentType.METHOD_TYPES;
            else
            {
                if (_fileType == ExportFileType.List)
                {
                    Text = Resources.ExportMethodDlg_ExportMethodDlg_Export_Transition_List;
                    listTypes = ExportInstrumentType.TRANSITION_LIST_TYPES;
                }
                else
                {
                    Text = Resources.ExportMethodDlg_ExportMethodDlg_Export_Isolation_List;
                    listTypes = ExportInstrumentType.ISOLATION_LIST_TYPES;
                    _exportProperties.MultiplexIsolationListCalculationTime = 20;   // Default 20 seconds to search for good multiplexed window ordering.
                }
                
                btnBrowseTemplate.Visible = false;
                labelTemplateFile.Visible = false;
                textTemplateFile.Visible = false;
                Height -= textTemplateFile.Bottom - comboTargetType.Bottom;
            }

            comboInstrument.Items.Clear();
            foreach (string typeName in listTypes)
                comboInstrument.Items.Add(typeName);

            // Init dialog values from settings.
            ExportStrategy = Helpers.ParseEnum(Settings.Default.ExportMethodStrategy, ExportStrategy.Single);

            IgnoreProteins = Settings.Default.ExportIgnoreProteins;

            // Start with method type as Standard until after instrument type is set
            comboTargetType.Items.Add(ExportMethodType.Standard.GetLocalizedString());
            comboTargetType.Items.Add(ExportMethodType.Scheduled.GetLocalizedString());
            comboTargetType.Items.Add(ExportMethodType.Triggered.GetLocalizedString());
            MethodType = ExportMethodType.Standard;

            // Set instrument type based on CE regression name for the document.
            string instrumentTypeName = document.Settings.TransitionSettings.Prediction.CollisionEnergy.Name;
            if (instrumentTypeName != null)
            {
                // Look for the first instrument type with the same prefix as the CE name
                string instrumentTypePrefix = instrumentTypeName.Split(' ')[0];
                // We still have some CE regressions that begin with ABI, while all instruments
                // have been changed to AB SCIEX
                if (Equals("ABI", instrumentTypePrefix)) // Not L10N
                    instrumentTypePrefix = "AB"; // Not L10N
                int i = -1;
                if (document.Settings.TransitionSettings.FullScan.IsEnabled)
                {
                    i = listTypes.IndexOf(typeName => typeName.StartsWith(instrumentTypePrefix) &&
                        ExportInstrumentType.IsFullScanInstrumentType(typeName));
                }
                if (i == -1)
                {
                    i = listTypes.IndexOf(typeName => typeName.StartsWith(instrumentTypePrefix));
                }
                if (i != -1)
                    InstrumentType = listTypes[i];
            }
            // If nothing found based on CE regression name, just use the first instrument in the list
            if (InstrumentType == null)
                InstrumentType = listTypes[0];

            // Reset method type based on what was used last and what the chosen instrument is capable of
            ExportMethodType mType = Helpers.ParseEnum(Settings.Default.ExportMethodType, ExportMethodType.Standard);
            if (mType == ExportMethodType.Triggered && !CanTrigger)
            {
                mType = ExportMethodType.Scheduled;
            }
            if (mType != ExportMethodType.Standard && !CanSchedule)
            {
                mType = ExportMethodType.Standard;
            }
            MethodType = mType;

            DwellTime = Settings.Default.ExportMethodDwellTime;
            RunLength = Settings.Default.ExportMethodRunLength;

            UpdateMaxTransitions();

            cbEnergyRamp.Checked = Settings.Default.ExportThermoEnergyRamp;
            cbTriggerRefColumns.Checked = Settings.Default.ExportThermoTriggerRef;
            cbExportMultiQuant.Checked = Settings.Default.ExportMultiQuant;
            textPrimaryCount.Text = Settings.Default.PrimaryTransitionCount.ToString(LocalizationHelper.CurrentCulture);
            // Reposition from design layout
            panelThermoColumns.Top = labelDwellTime.Top;
            panelThermoRt.Top = panelThermoColumns.Top - (int)(panelThermoRt.Height*0.8);
            panelAbSciexTOF.Top = textDwellTime.Top + (textDwellTime.Height - panelAbSciexTOF.Height)/2;
            panelTriggered.Top = textDwellTime.Top + (textDwellTime.Height - panelTriggered.Height)/2;

            // Add optimizable regressions
            comboOptimizing.Items.Add(ExportOptimize.NONE);
            comboOptimizing.Items.Add(ExportOptimize.CE);
            if (document.Settings.TransitionSettings.Prediction.DeclusteringPotential != null)
                comboOptimizing.Items.Add(ExportOptimize.DP);
            comboOptimizing.SelectedIndex = 0;

            cbExportMultiQuant.Checked = Settings.Default.ExportMultiQuant;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            _recalcMethodCountStatus = RecalcMethodCountStatus.waiting;
            CalcMethodCount();

            base.OnHandleCreated(e);
        }

        public string InstrumentType
        {
            get { return _instrumentType; }
            set
            {
                _instrumentType = value;

                // If scheduled method selected
                if (!Equals(ExportMethodType.Standard.GetLocalizedString().ToLower(),
                        comboTargetType.SelectedItem.ToString().ToLower()))
                {
                    // If single window state changing, and it is no longer possible to
                    // schedule, then switch back to a standard method.
                    if (ExportInstrumentType.IsSingleWindowInstrumentType(_instrumentType) != IsSingleWindowInstrument && !CanSchedule)
                        MethodType = ExportMethodType.Standard;                        

                }
                comboInstrument.SelectedItem = _instrumentType;
            }
        }

        public bool IsSingleWindowInstrument
        {
            get { return ExportInstrumentType.IsSingleWindowInstrumentType(InstrumentType); }
        }

        public bool IsInclusionListMethod
        {
            get { return IsFullScanInstrument && ExportInstrumentType.IsInclusionListMethod(_document); }
        }

        public bool IsSingleDwellInstrument
        {
            get { return IsSingleDwellInstrumentType(InstrumentType); }
        }

        private static bool IsSingleDwellInstrumentType(string type)
        {
            return Equals(type, ExportInstrumentType.AGILENT_TOF) ||
                   Equals(type, ExportInstrumentType.SHIMADZU) ||
                   Equals(type, ExportInstrumentType.THERMO) ||
                   Equals(type, ExportInstrumentType.THERMO_QUANTIVA) ||
                   Equals(type, ExportInstrumentType.THERMO_TSQ) ||
                   Equals(type, ExportInstrumentType.THERMO_LTQ) ||
                   Equals(type, ExportInstrumentType.THERMO_Q_EXACTIVE) ||
                   Equals(type, ExportInstrumentType.WATERS) ||
                   Equals(type, ExportInstrumentType.WATERS_XEVO) ||
                   Equals(type, ExportInstrumentType.WATERS_QUATTRO_PREMIER) ||
                   // For AbSciex's TOF 5600 and QSTAR instruments, the dwell (accumulation) time
                   // given in the template method is used. So we will not display the 
                   // "Dwell Time" text box.
                   Equals(type, ExportInstrumentType.ABI_TOF);
        }

        private bool IsAlwaysScheduledInstrument
        {
            get
            {
                if (!CanScheduleInstrumentType)
                    return false;

                var type = InstrumentType;
                return Equals(type, ExportInstrumentType.SHIMADZU) ||
                       Equals(type, ExportInstrumentType.THERMO_TSQ) ||
                       Equals(type, ExportInstrumentType.THERMO_QUANTIVA) ||
                       Equals(type, ExportInstrumentType.WATERS) ||
                       Equals(type, ExportInstrumentType.WATERS_XEVO) ||
                       Equals(type, ExportInstrumentType.WATERS_QUATTRO_PREMIER) ||
                       // LTQ can only schedule for inclusion lists, but then it always
                       // requires start and stop times.
                       Equals(type, ExportInstrumentType.THERMO_LTQ);
                       // This will only happen for ABI TOF with inclusion lists, since
                       // MRM-HR cannot yet be scheduled, and ABI TOF can either be scheduled
                       // or unscheduled when exporting inclusion lists.
//                       Equals(type, ExportInstrumentType.ABI_TOF); 
            }
        }

        private bool CanScheduleInstrumentType
        {
            get { return ExportInstrumentType.CanScheduleInstrumentType(InstrumentType, _document); }
        }

        private bool CanSchedule
        {
            get { return ExportInstrumentType.CanSchedule(InstrumentType, _document); }
        }

        private bool CanTriggerInstrumentType
        {
            get { return ExportInstrumentType.CanTriggerInstrumentType(InstrumentType); }
        }

        private bool CanTrigger
        {
            get { return CanTriggerReplicate(null); }
        }

        private bool CanTriggerReplicate(int? replicateIndex)
        {
            return ExportInstrumentType.CanTrigger(InstrumentType, _document, replicateIndex);
        }

        private ExportSchedulingAlgorithm SchedulingAlgorithm
        {
            set { _exportProperties.SchedulingAlgorithm = value; }
        }

        private int? SchedulingReplicateNum
        {
            set { _exportProperties.SchedulingReplicateNum = value; }
        }

        public bool IsFullScanInstrument
        {
            get { return ExportInstrumentType.IsFullScanInstrumentType(InstrumentType);  }
        }

        public ExportStrategy ExportStrategy
        {
            get { return _exportProperties.ExportStrategy; }
            set
            {
                _exportProperties.ExportStrategy = value;

                switch (_exportProperties.ExportStrategy)
                {
                    case ExportStrategy.Single:
                        radioSingle.Checked = true;
                        break;
                    case ExportStrategy.Protein:
                        radioProtein.Checked = true;
                        break;
                    case ExportStrategy.Buckets:
                        radioBuckets.Checked = true;
                        break;
                }
            }
        }

        public string OptimizeType
        {
            get { return _exportProperties.OptimizeType; }
            set
            {
                _exportProperties.OptimizeType = value;
                comboOptimizing.SelectedItem = _exportProperties.OptimizeType;
            }
        }

        public double OptimizeStepSize
        {
            get { return _exportProperties.OptimizeStepSize; }
            set
            {
                _exportProperties.OptimizeStepSize = value;
            }
        }

        public int OptimizeStepCount
        {
            get { return _exportProperties.OptimizeStepCount; }
            set
            {
                _exportProperties.OptimizeStepCount = value;
            }
        }

        public bool IgnoreProteins
        {
            get { return _exportProperties.IgnoreProteins; }
            set
            {
                _exportProperties.IgnoreProteins = value && ExportStrategy == ExportStrategy.Buckets;
                cbIgnoreProteins.Checked = _exportProperties.IgnoreProteins;
            }
        }

        public bool AddEnergyRamp
        {
            get { return _exportProperties.AddEnergyRamp; }
            set
            {
                _exportProperties.AddEnergyRamp = cbEnergyRamp.Checked = value;
            }
        }

        public bool AddTriggerReference
        {
            get { return _exportProperties.AddTriggerReference; }
            set
            {
                _exportProperties.AddTriggerReference = cbTriggerRefColumns.Checked = value;
            }
        }

        public bool ExportMultiQuant
        {
            get { return _exportProperties.ExportMultiQuant; }
            set { _exportProperties.ExportMultiQuant = cbExportMultiQuant.Checked = value; }
        }

        private void UpdateThermoColumns(ExportMethodType targetType)
        {
            panelThermoColumns.Visible = targetType == ExportMethodType.Scheduled &&
                InstrumentType == ExportInstrumentType.THERMO;
        }

        private void UpdateAbSciexControls()
        {
            panelAbSciexTOF.Visible = InstrumentType == ExportInstrumentType.ABI_TOF;
        }

        private void UpdateThermoRtControls(ExportMethodType targetType)
        {
            panelThermoRt.Visible =
                InstrumentType == ExportInstrumentType.THERMO_QUANTIVA ||
                (targetType == ExportMethodType.Scheduled && InstrumentType == ExportInstrumentType.THERMO);
            if (panelThermoColumns.Visible)
            {
                panelThermoRt.Top = panelThermoColumns.Top - (int)(panelThermoRt.Height * 0.8);
            }
            else
            {
                panelThermoRt.Top = labelDwellTime.Visible
                    ? labelDwellTime.Top - panelThermoRt.Height
                    : labelDwellTime.Top + (panelThermoRt.Height / 2);
            }
        }

        private void UpdateMaxTransitions()
        {
            try
            {
                string maxTran = IsFullScanInstrument
                                     ? Settings.Default.ExportMethodMaxPrec
                                     : Settings.Default.ExportMethodMaxTran;
                if (string.IsNullOrEmpty(maxTran))
                    MaxTransitions = null;
                else
                    MaxTransitions = int.Parse(maxTran, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                MaxTransitions = null;
            }
        }

        public ExportMethodType MethodType
        {
            get { return _exportProperties.MethodType; }
            set
            {
                _exportProperties.MethodType = value;
                comboTargetType.SelectedItem = _exportProperties.MethodType.GetLocalizedString();
            }
        }

        public int PrimaryCount
        {
            get { return _exportProperties.PrimaryTransitionCount; }
            set
            {
                _exportProperties.PrimaryTransitionCount = value;
                textPrimaryCount.Text = _exportProperties.PrimaryTransitionCount.ToString(LocalizationHelper.CurrentCulture);
            }
        }

        /// <summary>
        /// Specific dwell time in milliseconds for non-scheduled runs
        /// </summary>
        public int DwellTime
        {
            get { return _exportProperties.DwellTime; }
            set
            {
                _exportProperties.DwellTime = value;
                textDwellTime.Text = _exportProperties.DwellTime.ToString(LocalizationHelper.CurrentCulture);
            }
        }

        /// <summary>
        /// Length of run in minutes for non-scheduled runs
        /// </summary>
        public double RunLength
        {
            get { return _exportProperties.RunLength; }
            set
            {
                _exportProperties.RunLength = value;
                textRunLength.Text = _exportProperties.RunLength.ToString(LocalizationHelper.CurrentCulture);
            }
        }

        /// <summary>
        /// Used for maximum transitions/precursors, maximum concurrent transitions/precursors for SRM/full-scan
        /// </summary>
        public int? MaxTransitions
        {
            get { return _exportProperties.MaxTransitions; }
            set
            {
                _exportProperties.MaxTransitions = value;
                textMaxTransitions.Text = (_exportProperties.MaxTransitions.HasValue
                    ? _exportProperties.MaxTransitions.Value.ToString(LocalizationHelper.CurrentCulture)
                    : string.Empty);
            }
        }

        public void OkDialog()
        {
            OkDialog(null);
        }

        public void OkDialog(string outputPath)
        {
            var helper = new MessageBoxHelper(this, true);

            _instrumentType = comboInstrument.SelectedItem.ToString();

            // Use variable for document to export, since code below may modify the document.
            SrmDocument documentExport = _document;

            string templateName = null;
            if (_fileType == ExportFileType.Method)
            {
                // Check for instruments that cannot do DIA.
                if (IsDia)
                {
                    if (Equals(InstrumentType, ExportInstrumentType.AGILENT_TOF) ||
                        Equals(InstrumentType, ExportInstrumentType.ABI_TOF) ||
                        Equals(InstrumentType, ExportInstrumentType.THERMO_LTQ))
                    {
                        helper.ShowTextBoxError(textTemplateFile, Resources.ExportMethodDlg_OkDialog_Export_of_DIA_method_is_not_supported_for__0__, InstrumentType);
                        return;
                    }
                }

                templateName = textTemplateFile.Text;
                if (string.IsNullOrEmpty(templateName))
                {
                    helper.ShowTextBoxError(textTemplateFile, Resources.ExportMethodDlg_OkDialog_A_template_file_is_required_to_export_a_method);
                    return;
                }
                if ((Equals(InstrumentType, ExportInstrumentType.AGILENT6400) ||
                    Equals(InstrumentType, ExportInstrumentType.BRUKER_TOF)) ?
                                                                                 !Directory.Exists(templateName) : !File.Exists(templateName))
                {
                    helper.ShowTextBoxError(textTemplateFile, Resources.ExportMethodDlg_OkDialog_The_template_file__0__does_not_exist, templateName);
                    return;
                }
                if (Equals(InstrumentType, ExportInstrumentType.AGILENT6400) &&
                    !AgilentMethodExporter.IsAgilentMethodPath(templateName))
                {
                    helper.ShowTextBoxError(textTemplateFile,
                                            Resources.ExportMethodDlg_OkDialog_The_folder__0__does_not_appear_to_contain_an_Agilent_QQQ_method_template_The_folder_is_expected_to_have_a_m_extension_and_contain_the_file_qqqacqmethod_xsd,
                                            templateName);
                    return;
                }
                if (Equals(InstrumentType, ExportInstrumentType.BRUKER_TOF) &&
                    !BrukerMethodExporter.IsBrukerMethodPath(templateName))
                {
                    helper.ShowTextBoxError(textTemplateFile,
                                            Resources.ExportMethodDlg_OkDialog_The_folder__0__does_not_appear_to_contain_a_Bruker_TOF_method_template___The_folder_is_expected_to_have_a__m_extension__and_contain_the_file_submethods_xml_,
                                            templateName);
                    return;
                }
            }

            if (Equals(InstrumentType, ExportInstrumentType.AGILENT_TOF) ||
                Equals(InstrumentType, ExportInstrumentType.ABI_TOF))
            {
                // Check that mass analyzer settings are set to TOF.
                if (documentExport.Settings.TransitionSettings.FullScan.IsEnabledMs && 
                    documentExport.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer != FullScanMassAnalyzerType.tof)
                {
                    MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_OkDialog_The_precursor_mass_analyzer_type_is_not_set_to__0__in_Transition_Settings_under_the_Full_Scan_tab, Resources.ExportMethodDlg_OkDialog_TOF));
                    return;
                }
                if (documentExport.Settings.TransitionSettings.FullScan.IsEnabledMsMs &&
                    documentExport.Settings.TransitionSettings.FullScan.ProductMassAnalyzer != FullScanMassAnalyzerType.tof)
                {
                    MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_OkDialog_The_product_mass_analyzer_type_is_not_set_to__0__in_Transition_Settings_under_the_Full_Scan_tab, Resources.ExportMethodDlg_OkDialog_TOF));
                    return;
                }                    
            }

            if (Equals(InstrumentType, ExportInstrumentType.THERMO_Q_EXACTIVE))
            {
                // Check that mass analyzer settings are set to Orbitrap.
                if (documentExport.Settings.TransitionSettings.FullScan.IsEnabledMs &&
                    documentExport.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer != FullScanMassAnalyzerType.orbitrap)
                {
                    MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_OkDialog_The_precursor_mass_analyzer_type_is_not_set_to__0__in_Transition_Settings_under_the_Full_Scan_tab, Resources.ExportMethodDlg_OkDialog_Orbitrap));
                    return;
                }
                if (documentExport.Settings.TransitionSettings.FullScan.IsEnabledMsMs &&
                    documentExport.Settings.TransitionSettings.FullScan.ProductMassAnalyzer != FullScanMassAnalyzerType.orbitrap)
                {
                    MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_OkDialog_The_product_mass_analyzer_type_is_not_set_to__0__in_Transition_Settings_under_the_Full_Scan_tab, Resources.ExportMethodDlg_OkDialog_Orbitrap));
                    return;
                }                    
            }

            if (!documentExport.HasAllRetentionTimeStandards() &&
                DialogResult.Cancel == MultiButtonMsgDlg.Show(
                    this,
                    TextUtil.LineSeparate(
                        Resources.ExportMethodDlg_OkDialog_The_document_does_not_contain_all_of_the_retention_time_standard_peptides,
                        Resources.ExportMethodDlg_OkDialog_You_will_not_be_able_to_use_retention_time_prediction_with_acquired_results,
                        Resources.ExportMethodDlg_OkDialog_Are_you_sure_you_want_to_continue),
                    Resources.ExportMethodDlg_OkDialog_OK))
            {
                return;
            }

            //This will populate _exportProperties
            if (!ValidateSettings(helper))
            {
                return;
            }

            // Full-scan method building ignores CE and DP regression values
            if (!IsFullScanInstrument)
            {
                // Check to make sure CE and DP match chosen instrument, and offer to use
                // the correct version for the instrument, if not.
                var predict = documentExport.Settings.TransitionSettings.Prediction;
                var ce = predict.CollisionEnergy;
                string ceName = (ce != null ? ce.Name : null);
                string ceNameDefault = _instrumentType;
                if (ceNameDefault.IndexOf(' ') != -1)
                    ceNameDefault = ceNameDefault.Substring(0, ceNameDefault.IndexOf(' '));
                bool ceInSynch = ceName != null && ceName.StartsWith(ceNameDefault);

                var dp = predict.DeclusteringPotential;
                string dpName = (dp != null ? dp.Name : null);
                string dpNameDefault = _instrumentType;
                if (dpNameDefault.IndexOf(' ') != -1)
                    dpNameDefault = dpNameDefault.Substring(0, dpNameDefault.IndexOf(' '));
                bool dpInSynch = true;
                if (_instrumentType == ExportInstrumentType.ABI)
                    dpInSynch = dpName != null && dpName.StartsWith(dpNameDefault);
                else
                    dpNameDefault = null; // Ignored for all other types

                if ((!ceInSynch && Settings.Default.CollisionEnergyList.Keys.Any(name => name.StartsWith(ceNameDefault)) ||
                    (!dpInSynch && Settings.Default.DeclusterPotentialList.Keys.Any(name => name.StartsWith(dpNameDefault)))))
                {
                    var sb = new StringBuilder(string.Format(Resources.ExportMethodDlg_OkDialog_The_settings_for_this_document_do_not_match_the_instrument_type__0__,
                                                             _instrumentType));
                    sb.AppendLine().AppendLine();
                    if (!ceInSynch)
                        sb.Append(Resources.ExportMethodDlg_OkDialog_Collision_Energy).Append(TextUtil.SEPARATOR_SPACE).AppendLine(ceName);
                    if (!dpInSynch)
                    {
                        sb.Append(Resources.ExportMethodDlg_OkDialog_Declustering_Potential).Append(TextUtil.SEPARATOR_SPACE)
                          .AppendLine(dpName ?? Resources.ExportMethodDlg_OkDialog_None);
                    }
                    sb.AppendLine().Append(Resources.ExportMethodDlg_OkDialog_Would_you_like_to_use_the_defaults_instead);
                    var result = MultiButtonMsgDlg.Show(this, sb.ToString(), MultiButtonMsgDlg.BUTTON_YES, MultiButtonMsgDlg.BUTTON_NO, true);
                    if (result == DialogResult.Yes)
                    {
                        documentExport = ChangeInstrumentTypeSettings(documentExport, ceNameDefault, dpNameDefault);
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        comboInstrument.Focus();
                        return;
                    }
                }
            }

            if (outputPath == null)
            {
                string title = Text;
                string ext = TextUtil.EXT_CSV;
                string filter = Resources.ExportMethodDlg_OkDialog_Method_File;

                switch (_fileType)
                {
                    case ExportFileType.List:
                        filter = Resources.ExportMethodDlg_OkDialog_Transition_List;
                        ext = ExportInstrumentType.TransitionListExtention(_instrumentType);
                        break;

                    case ExportFileType.IsolationList:
                        filter = Resources.ExportMethodDlg_OkDialog_Isolation_List;
                        break;

                    case ExportFileType.Method:
                        title = string.Format(Resources.ExportMethodDlg_OkDialog_Export__0__Method, _instrumentType);
                        ext = ExportInstrumentType.MethodExtension(_instrumentType);
                        break;
                }

                using (var dlg = new SaveFileDialog
                    {
                        Title = title,
                        InitialDirectory = Settings.Default.ExportDirectory,
                        OverwritePrompt = true,
                        DefaultExt = ext,
                        Filter = TextUtil.FileDialogFilterAll(filter, ext)
                    })
                {
                    if (dlg.ShowDialog(this) == DialogResult.Cancel)
                    {
                        return;
                    }

                    outputPath = dlg.FileName;
                }
            }

            Settings.Default.ExportDirectory = Path.GetDirectoryName(outputPath);

            // Set ShowMessages property on ExportDlgProperties to true
            // so that we see the progress dialog during the export process
            var wasShowMessageValue = _exportProperties.ShowMessages;
            _exportProperties.ShowMessages = true;
            try
            {
                _exportProperties.ExportFile(_instrumentType, _fileType, outputPath, documentExport, templateName);
            }
            catch(UnauthorizedAccessException x)
            {
                MessageDlg.Show(this, x.Message);
                _exportProperties.ShowMessages = wasShowMessageValue;
                return;
            }
            catch (IOException x)
            {
                MessageDlg.Show(this, x.Message);
                _exportProperties.ShowMessages = wasShowMessageValue;
                return;
            }

            // Successfully completed dialog.  Store the values in settings.
            Settings.Default.ExportInstrumentType = _instrumentType;
            Settings.Default.ExportMethodStrategy = ExportStrategy.ToString();
            Settings.Default.ExportIgnoreProteins = IgnoreProteins;
            if (IsFullScanInstrument)
            {
                Settings.Default.ExportMethodMaxPrec = (MaxTransitions.HasValue ?
                    MaxTransitions.Value.ToString(CultureInfo.InvariantCulture) : null);                
            }
            else
            {
                Settings.Default.ExportMethodMaxTran = (MaxTransitions.HasValue ?
                    MaxTransitions.Value.ToString(CultureInfo.InvariantCulture) : null);
            }
            Settings.Default.ExportMethodType = _exportProperties.MethodType.ToString();
            if (textPrimaryCount.Visible)
                Settings.Default.PrimaryTransitionCount = PrimaryCount;
            if (textDwellTime.Visible)
                Settings.Default.ExportMethodDwellTime = DwellTime;
            if (textRunLength.Visible)
                Settings.Default.ExportMethodRunLength = RunLength;
            if (panelThermoColumns.Visible)
            {
                Settings.Default.ExportThermoEnergyRamp = AddEnergyRamp;
                Settings.Default.ExportThermoTriggerRef = AddTriggerReference;
            }
            if (_fileType == ExportFileType.Method)
                Settings.Default.ExportMethodTemplateList.SetValue(new MethodTemplateFile(_instrumentType, templateName));
            if (cbExportMultiQuant.Visible)
                Settings.Default.ExportMultiQuant = ExportMultiQuant;

            DialogResult = DialogResult.OK;
            Close();
        }


        /// <summary>
        /// This function will validate all the settings required for exporting a method,
        /// placing the values on the ExportDlgProperties _exportProperties. It returns
        /// boolean whether or not it succeeded. It can show MessageBoxes or not based
        /// on a parameter.
        /// </summary>
        public bool ValidateSettings(MessageBoxHelper helper)
        {
            // ReSharper disable ConvertIfStatementToConditionalTernaryExpression
            if (radioSingle.Checked)
                _exportProperties.ExportStrategy = ExportStrategy.Single;
            else if (radioProtein.Checked)
                _exportProperties.ExportStrategy = ExportStrategy.Protein;
            else
                _exportProperties.ExportStrategy = ExportStrategy.Buckets;
            // ReSharper restore ConvertIfStatementToConditionalTernaryExpression

            _exportProperties.IgnoreProteins = cbIgnoreProteins.Checked;
            _exportProperties.FullScans = _document.Settings.TransitionSettings.FullScan.IsEnabledMsMs;
            _exportProperties.AddEnergyRamp = panelThermoColumns.Visible && cbEnergyRamp.Checked;
            _exportProperties.AddTriggerReference = panelThermoColumns.Visible && cbTriggerRefColumns.Checked;

            _exportProperties.ExportMultiQuant = panelAbSciexTOF.Visible && cbExportMultiQuant.Checked;

            _exportProperties.RetentionStartAndEnd = panelThermoRt.Visible && cbUseStartAndEndRts.Checked;

            _exportProperties.Ms1Scan = _document.Settings.TransitionSettings.FullScan.IsEnabledMs &&
                                        _document.Settings.TransitionSettings.FullScan.IsEnabledMsMs;

            _exportProperties.InclusionList = IsInclusionListMethod;

            _exportProperties.MsAnalyzer =
                TransitionFullScan.MassAnalyzerToString(
                    _document.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer);
            _exportProperties.MsMsAnalyzer =
                TransitionFullScan.MassAnalyzerToString(
                    _document.Settings.TransitionSettings.FullScan.ProductMassAnalyzer);

            _exportProperties.OptimizeType = comboOptimizing.SelectedItem == null ? ExportOptimize.NONE : comboOptimizing.SelectedItem.ToString();
            var prediction = _document.Settings.TransitionSettings.Prediction;
            if (Equals(_exportProperties.OptimizeType, ExportOptimize.CE))
            {
                var regression = prediction.CollisionEnergy;
                _exportProperties.OptimizeStepSize = regression.StepSize;
                _exportProperties.OptimizeStepCount = regression.StepCount;
            }
            else if (Equals(_exportProperties.OptimizeType, ExportOptimize.DP))
            {
                var regression = prediction.DeclusteringPotential;
                _exportProperties.OptimizeStepSize = regression.StepSize;
                _exportProperties.OptimizeStepCount = regression.StepCount;
            }
            else
            {
                _exportProperties.OptimizeType = null;
                _exportProperties.OptimizeStepSize = _exportProperties.OptimizeStepCount = 0;
            }

            string maxTran = textMaxTransitions.Text;
            if (string.IsNullOrEmpty(maxTran))
            {
                if (_exportProperties.ExportStrategy == ExportStrategy.Buckets)
                {
                    helper.ShowTextBoxError(textMaxTransitions, Resources.ExportMethodDlg_ValidateSettings__0__must_contain_a_value);
                    return false;
                }
                _exportProperties.MaxTransitions = null;
            }

            int maxVal;
            // CONSIDER: Better error message when instrument limitation encountered?
            int maxInstrumentTrans = _document.Settings.TransitionSettings.Instrument.MaxTransitions ??
                                        TransitionInstrument.MAX_TRANSITION_MAX;
            int minTrans = IsFullScanInstrument
                               ? AbstractMassListExporter.MAX_TRANS_PER_INJ_MIN
                               : MethodExporter.MAX_TRANS_PER_INJ_MIN_TLTQ;

            if (_exportProperties.ExportStrategy != ExportStrategy.Buckets)
                maxVal = maxInstrumentTrans;
            else if (!helper.ValidateNumberTextBox(textMaxTransitions, minTrans, maxInstrumentTrans, out maxVal))
                return false;

            // Make sure all the transitions of all precursors can fit into a single document,
            // but not if this is a full-scan instrument, because then the maximum is refering
            // to precursors and not transitions.
            if (!IsFullScanInstrument && !ValidatePrecursorFit(_document, maxVal, helper.ShowMessages))
                return false;
            _exportProperties.MaxTransitions = maxVal;

            _exportProperties.MethodType = ExportMethodTypeExtension.GetEnum(comboTargetType.SelectedItem.ToString());

            if (textPrimaryCount.Visible)
            {
                int primaryCount;
                if (!helper.ValidateNumberTextBox(textPrimaryCount, AbstractMassListExporter.PRIMARY_COUNT_MIN, AbstractMassListExporter.PRIMARY_COUNT_MAX, out primaryCount))
                    return false;

                _exportProperties.PrimaryTransitionCount = primaryCount;
            }
            if (textDwellTime.Visible)
            {
                int dwellTime;
                if (!helper.ValidateNumberTextBox(textDwellTime, AbstractMassListExporter.DWELL_TIME_MIN, AbstractMassListExporter.DWELL_TIME_MAX, out dwellTime))
                    return false;

                _exportProperties.DwellTime = dwellTime;
            }
            if (textRunLength.Visible)
            {
                double runLength;
                if (!helper.ValidateDecimalTextBox(textRunLength, AbstractMassListExporter.RUN_LENGTH_MIN, AbstractMassListExporter.RUN_LENGTH_MAX, out runLength))
                    return false;

                _exportProperties.RunLength = runLength;
            }

            // If export method type is scheduled, and allows multiple scheduling options
            // ask the user which to use.
            if (_exportProperties.MethodType != ExportMethodType.Standard && HasMultipleSchedulingOptions(_document))
            {
                if (!helper.ShowMessages)
                {
                    // CONSIDER: Kind of a hack, but pick some reasonable defaults.  The user
                    //           may decide otherwise later, but this is the best we can do
                    //           without asking.
                    if (!_document.Settings.HasResults || Settings.Default.ScheduleAvergeRT)
                        SchedulingAlgorithm = ExportSchedulingAlgorithm.Average;
                    else
                    {
                        SchedulingAlgorithm = ExportSchedulingAlgorithm.Single;
                        SchedulingReplicateNum = _document.Settings.MeasuredResults.Chromatograms.Count - 1;
                    }
                }
                else
                {
                    using (var schedulingOptionsDlg = new SchedulingOptionsDlg(_document, i =>
                            _exportProperties.MethodType != ExportMethodType.Triggered || CanTriggerReplicate(i)))
                    {
                        if (schedulingOptionsDlg.ShowDialog(this) != DialogResult.OK)
                            return false;

                        SchedulingAlgorithm = schedulingOptionsDlg.Algorithm;
                        SchedulingReplicateNum = schedulingOptionsDlg.ReplicateNum;
                    }
                }
            }

            return true;
        }

        private static bool HasMultipleSchedulingOptions(SrmDocument document)
        {
            // No scheduling from data, if no data is present
            if (!document.Settings.HasResults || !document.Settings.PeptideSettings.Prediction.UseMeasuredRTs)
                return false;

            // If multipe non-optimization data sets are present, allow user to choose.
            var chromatagrams = document.Settings.MeasuredResults.Chromatograms;
            int sched = chromatagrams.Count(chromatogramSet => chromatogramSet.OptimizationFunction == null);

            if (sched > 1)
                return true;
            // Otherwise, if no non-optimization data is present, but multiple optimization
            // sets are available, allow user to choose from them.
            return (sched == 0 && chromatagrams.Count > 1);
        }

        private bool ValidatePrecursorFit(SrmDocument document, int maxTransitions, bool showMessages)
        {
            string messageFormat = (OptimizeType == null ?
                Resources.ExportMethodDlg_ValidatePrecursorFit_The_precursor__0__for_the_peptide__1__has__2__transitions_which_exceeds_the_current_maximum__3__ :
                Resources.ExportMethodDlg_ValidatePrecursorFit_The_precursor__0__for_the_peptide__1__requires__2__transitions_to_optimize_which_exceeds_the_current_maximum__3__);
            foreach (var nodeGroup in document.TransitionGroups)
            {
                int tranRequired = nodeGroup.Children.Count;
                if (OptimizeType != null)
                    tranRequired *= OptimizeStepCount * 2 + 1;
                if (tranRequired > maxTransitions)
                {
                    if (showMessages)
                    {
                        MessageDlg.Show(this, string.Format(messageFormat,
                            SequenceMassCalc.PersistentMZ(nodeGroup.PrecursorMz) + Transition.GetChargeIndicator(nodeGroup.TransitionGroup.PrecursorCharge),
                            nodeGroup.TransitionGroup.Peptide.Sequence,
                            tranRequired,
                            maxTransitions));
                    }
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Changes collision energy and declustering potential settings to their
        /// default values for an instrument type.
        /// </summary>
        /// <param name="document">Document to change</param>
        /// <param name="ceNameDefault">Default name for CE</param>
        /// <param name="dpNameDefault">Default name for DP</param>
        private static SrmDocument ChangeInstrumentTypeSettings(SrmDocument document, string ceNameDefault, string dpNameDefault)
        {
            var ceList = Settings.Default.CollisionEnergyList;
            CollisionEnergyRegression ce;
            if (!ceList.TryGetValue(ceNameDefault, out ce))
            {
                foreach (var ceDefault in ceList.GetDefaults())
                {
                    if (ceDefault.Name.StartsWith(ceNameDefault))
                        ce = ceDefault;
                }
            }
            var dpList = Settings.Default.DeclusterPotentialList;
            DeclusteringPotentialRegression dp = null;
            if (dpNameDefault != null && !dpList.TryGetValue(dpNameDefault, out dp))
            {
                foreach (var dpDefault in dpList.GetDefaults())
                {
                    if (dpDefault.Name.StartsWith(dpNameDefault))
                        dp = dpDefault;
                }
            }

            return document.ChangeSettings(document.Settings.ChangeTransitionPrediction(
                predict =>
                    {
                        if (ce != null)
                            predict = predict.ChangeCollisionEnergy(ce);
                        if (dp != null)
                            predict = predict.ChangeDeclusteringPotential(dp);
                        return predict;
                    }));
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            OkDialog(null);
        }

        private void radioSingle_CheckedChanged(object sender, EventArgs e)
        {
            StrategyCheckChanged();
        }

        private void radioProtein_CheckedChanged(object sender, EventArgs e)
        {
            StrategyCheckChanged();
        }

        private void radioBuckets_CheckedChanged(object sender, EventArgs e)
        {
            StrategyCheckChanged();
        }

        private bool IsDia
        {
            get
            {
                return IsFullScanInstrument &&
                    _document.Settings.TransitionSettings.FullScan.AcquisitionMethod == FullScanAcquisitionMethod.DIA;
            }
        }

        private void StrategyCheckChanged()
        {
            if (IsDia && !radioSingle.Checked)
            {
                MessageDlg.Show(this, Resources.ExportMethodDlg_StrategyCheckChanged_Only_one_method_can_be_exported_in_DIA_mode);
                radioSingle.Checked = true;
            }

            textMaxTransitions.Enabled = radioBuckets.Checked;
            if (!textMaxTransitions.Enabled)
                textMaxTransitions.Clear();
            cbIgnoreProteins.Enabled = radioBuckets.Checked;
            if (!radioBuckets.Checked)
                cbIgnoreProteins.Checked = false;

            if (radioSingle.Checked)
            {
                labelMethodNum.Text = 1.ToString(LocalizationHelper.CurrentCulture);
            }
            else
            {
                CalcMethodCount();
            }
        }

        private void comboInstrument_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool wasFullScanInstrument = IsFullScanInstrument;

            _instrumentType = comboInstrument.SelectedItem.ToString();

            // Temporary code until we support Agilent export of DIA isolation lists.
            if (Equals(_instrumentType, ExportInstrumentType.AGILENT_TOF) && IsDia)
            {
                MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_comboInstrument_SelectedIndexChanged_Export_of_DIA_isolation_lists_is_not_yet_supported_for__0__,
                                                    _instrumentType));
                comboInstrument.SelectedItem = ExportInstrumentType.THERMO_Q_EXACTIVE;
                return;
            }

            if (wasFullScanInstrument != IsFullScanInstrument)
                UpdateMaxTransitions();

            MethodTemplateFile templateFile;
            textTemplateFile.Text = Settings.Default.ExportMethodTemplateList.TryGetValue(_instrumentType, out templateFile)
                ? templateFile.FilePath
                : string.Empty;

            var targetType = ExportMethodTypeExtension.GetEnum(comboTargetType.SelectedItem.ToString());
            if (targetType == ExportMethodType.Triggered && !CanTrigger && CanSchedule)
            {
                comboTargetType.SelectedItem = ExportMethodType.Scheduled.GetLocalizedString();
                // Change in target type will update the instrument controls and calc method count
                return;
            }
            if (targetType != ExportMethodType.Standard && !CanSchedule)
            {
                comboTargetType.SelectedItem = ExportMethodType.Standard.GetLocalizedString();
                // Change in target type will update the instrument controls and calc method count
                return;
            }                

            // Always keep the comboTargetType (Method type) enabled. Throw and error if the 
            // user selects "Scheduled" or "Triggered" and it is not supported by the instrument.
            // comboTargetType.Enabled = CanScheduleInstrumentType;
            
            UpdateInstrumentControls(targetType);

            CalcMethodCount();
        }

        private void comboTargetType_SelectedIndexChanged(object sender, EventArgs e)
        {
            var targetType = ExportMethodTypeExtension.GetEnum(comboTargetType.SelectedItem.ToString());
            bool standard = (targetType == ExportMethodType.Standard);
            bool triggered = (targetType == ExportMethodType.Triggered);
            if (!standard && !VerifySchedulingAllowed(triggered))
            {
                comboTargetType.SelectedItem = ExportMethodType.Standard.GetLocalizedString();
                targetType = ExportMethodType.Standard;
            }

            UpdateInstrumentControls(targetType);

            CalcMethodCount();
        }

        private void UpdateInstrumentControls(ExportMethodType targetType)
        {
            bool standard = (targetType == ExportMethodType.Standard);
            bool triggered = (targetType == ExportMethodType.Triggered);

            if (triggered && !(InstrumentType == ExportInstrumentType.ABI || InstrumentType == ExportInstrumentType.ABI_QTRAP))
            {
                comboOptimizing.Enabled = false;
            }
            else
            {
                comboOptimizing.Enabled = !IsFullScanInstrument;
            }
            if (!comboOptimizing.Enabled)
            {
                OptimizeType = ExportOptimize.NONE;
            }

            UpdateTriggerControls(targetType);
            UpdateDwellControls(standard);
            UpdateThermoColumns(targetType);
            UpdateAbSciexControls();
            UpdateThermoRtControls(targetType);
            UpdateMaxLabel(standard);
        }

        private void textPrimaryCount_TextChanged(object sender, EventArgs e)
        {
            CalcMethodCount();
        }

        private bool VerifySchedulingAllowed(bool triggered)
        {
            if (IsDia)
            {
                MessageDlg.Show(this, Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Scheduled_methods_are_not_yet_supported_for_DIA_acquisition);
                return false;
            }
            if (triggered)
            {
                if (!CanTriggerInstrumentType)
                {
                    // Give a clearer message for the Thermo TSQ, since it does actually support triggered acquisition,
                    // but we are unable to export directly to mehtods.
                    if (Equals(InstrumentType, ExportInstrumentType.THERMO_TSQ))
                        MessageDlg.Show(this, TextUtil.LineSeparate(string.Format(Resources.ExportMethodDlg_VerifySchedulingAllowed_The__0__instrument_lacks_support_for_direct_method_export_for_triggered_acquisition_, InstrumentType),
                                                                    string.Format(Resources.ExportMethodDlg_VerifySchedulingAllowed_You_must_export_a__0__transition_list_and_manually_import_it_into_a_method_file_using_vendor_software_, ExportInstrumentType.THERMO)));
                    else
                        MessageDlg.Show(this, string.Format(Resources.ExportMethodDlg_VerifySchedulingAllowed_The_instrument_type__0__does_not_support_triggered_acquisition_, InstrumentType));
                    return false;
                }
                if (!_document.Settings.HasResults && !_document.Settings.HasLibraries)
                {
                    MessageDlg.Show(this, Resources.ExportMethodDlg_VerifySchedulingAllowed_Triggered_acquistion_requires_a_spectral_library_or_imported_results_in_order_to_rank_transitions_);
                    return false;
                }
                if (!CanTrigger)
                {
                    MessageDlg.Show(this, Resources.ExportMethodDlg_VerifySchedulingAllowed_The_current_document_contains_peptides_without_enough_information_to_rank_transitions_for_triggered_acquisition_);
                    return false;
                }
            }
            if (!CanSchedule)
            {
                var prediction = _document.Settings.PeptideSettings.Prediction;

                // The "Method type" combo box is always enabled.  Display error message if the user
                // selects "Scheduled" for an instrument that does not support scheduled methods (e.g LTQ, ABI TOF)
                // However, if we are exporting inclusion lists (MS1 filtering enabled AND MS2 filtering disabled) 
                // the user should be able to select "Scheduled" for LTQ and ABI TOF instruments.
                if (!CanScheduleInstrumentType)
                {
                    MessageDlg.Show(this, SCHED_NOT_SUPPORTED_ERR_TXT);
                }
                else if (prediction.RetentionTime == null)
                {
                    if (prediction.UseMeasuredRTs)
                        MessageDlg.Show(this, Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_To_export_a_scheduled_list_you_must_first_choose_a_retention_time_predictor_in_Peptide_Settings_Prediction_or_import_results_for_all_peptides_in_the_document);
                    else
                        MessageDlg.Show(this, Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_To_export_a_scheduled_list_you_must_first_choose_a_retention_time_predictor_in_Peptide_Settings_Prediction);
                }
                else if (!prediction.RetentionTime.Calculator.IsUsable)
                {
                    MessageDlg.Show(this, TextUtil.LineSeparate(
                        Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Retention_time_prediction_calculator_is_unable_to_score,
                        Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Check_the_calculator_settings));
                }
                else if (!prediction.RetentionTime.IsUsable)
                {
                    MessageDlg.Show(this, TextUtil.LineSeparate(
                        Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Retention_time_predictor_is_unable_to_auto_calculate_a_regression,
                        Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_Check_to_make_sure_the_document_contains_times_for_all_of_the_required_standard_peptides));
                }
                else
                {
                    MessageDlg.Show(this, Resources.ExportMethodDlg_comboTargetType_SelectedIndexChanged_To_export_a_scheduled_list_you_must_first_import_results_for_all_peptides_in_the_document);
                }
                return false;
            }
            return true;
        }

        private void UpdateTriggerControls(ExportMethodType targetType)
        {
            panelTriggered.Visible = (targetType == ExportMethodType.Triggered);
        }

        private void UpdateMaxLabel(bool standard)
        {
            if (standard)
            {
                labelMaxTransitions.Text = IsFullScanInstrument
                    ? PREC_PER_SAMPLE_INJ_TXT
                    : TRANS_PER_SAMPLE_INJ_TXT;
            }
            else
            {
                labelMaxTransitions.Text = IsFullScanInstrument
                    ? CONCUR_PREC_TXT
                    : CONCUR_TRANS_TXT;
            }
        }

        private enum RecalcMethodCountStatus { waiting, running, pending }
        private RecalcMethodCountStatus _recalcMethodCountStatus = RecalcMethodCountStatus.waiting;

        private void CalcMethodCount()
        {
            if (InstrumentType == null)
                return;

            if (IsDia)
            {
                labelMethodNum.Text = 1.ToString(LocalizationHelper.CurrentCulture);
                return;
            }

            if (_recalcMethodCountStatus != RecalcMethodCountStatus.waiting || !IsHandleCreated)
            {
                _recalcMethodCountStatus = RecalcMethodCountStatus.pending;
                return;
            }

            var helper = new MessageBoxHelper(this, false);

            if (!ValidateSettings(helper) || comboInstrument.SelectedItem == null)
            {
                labelMethodNum.Text = string.Empty;
                return;
            }

// ReSharper disable LocalizableElement
            labelMethodNum.Text = "..."; // Not L10N
// ReSharper restore LocalizableElement

            _recalcMethodCountStatus = RecalcMethodCountStatus.running;

            var recalcMethodCount = new RecalcMethodCountCaller(RecalcMethodCount);
            string instrument = comboInstrument.SelectedItem.ToString();
            recalcMethodCount.BeginInvoke(_exportProperties, instrument, _fileType, _document, null, null);
        }

        private delegate void RecalcMethodCountCaller(ExportDlgProperties exportProperties,
            string instrument, ExportFileType fileType, SrmDocument document);

        private void RecalcMethodCount(ExportDlgProperties exportProperties,
            string instrument, ExportFileType fileType, SrmDocument document)
        {
            AbstractMassListExporter exporter = null;
            try
            {
                exporter = exportProperties.ExportFile(instrument, fileType, null, document, null);
            }
            catch (IOException)
            {
            }
            catch(ADOException)
            {
            }

            int? methodCount = null;
            if (exporter != null)
                methodCount = exporter.MemoryOutput.Count;
            // Switch back to the UI thread to update the form
            try
            {
                if (IsHandleCreated)
                    Invoke(new Action<int?>(UpdateMethodCount), methodCount);
            }
// ReSharper disable EmptyGeneralCatchClause
            catch (Exception)
// ReSharper restore EmptyGeneralCatchClause
            {
                // If disposed, then no need to update the UI
            }
        }

        private void UpdateMethodCount(int? methodCount)
        {
            labelMethodNum.Text = methodCount.HasValue
                ? methodCount.Value.ToString(LocalizationHelper.CurrentCulture)
                : string.Empty;

            var recalcMethodCountStatus = _recalcMethodCountStatus;
            _recalcMethodCountStatus = RecalcMethodCountStatus.waiting;
            if (recalcMethodCountStatus == RecalcMethodCountStatus.pending)
                CalcMethodCount();
        }

        private void UpdateDwellControls(bool standard)
        {
            bool showDwell = false;
            bool showRunLength = false;
            if (standard && !IsDia)
            {
                if (!IsSingleDwellInstrument)
                {
                    labelDwellTime.Text = DWELL_TIME_TXT;
                    showDwell = true;
                }
                else if (IsAlwaysScheduledInstrument)
                {
                    labelDwellTime.Text = RUN_DURATION_TXT;
                    showRunLength = true;                    
                }
            }
            labelDwellTime.Visible = showDwell || showRunLength;
            textDwellTime.Visible = showDwell;
            textRunLength.Visible = showRunLength;
        }

        private void btnBrowseTemplate_Click(object sender, EventArgs e)
        {
            string templateName = textTemplateFile.Text;
            if (Equals(InstrumentType, ExportInstrumentType.AGILENT6400) ||
                Equals(InstrumentType, ExportInstrumentType.BRUKER_TOF))
            {
                using (var chooseDirDialog = new FolderBrowserDialog
                    {
                        Description = Resources.ExportMethodDlg_btnBrowseTemplate_Click_Method_Template,
                    })
                {
                    if (!string.IsNullOrEmpty(templateName))
                    {
                        chooseDirDialog.SelectedPath = templateName;
                    }

                    if (chooseDirDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        templateName = chooseDirDialog.SelectedPath;
                        if (Equals(InstrumentType, ExportInstrumentType.AGILENT6400) &&
                            !AgilentMethodExporter.IsAgilentMethodPath(templateName))
                        {
                            MessageDlg.Show(this, Resources.ExportMethodDlg_btnBrowseTemplate_Click_The_chosen_folder_does_not_appear_to_contain_an_Agilent_QQQ_method_template_The_folder_is_expected_to_have_a_m_extension_and_contain_the_file_qqqacqmethod_xsd);
                            return;
                        }
                        else if (Equals(InstrumentType, ExportInstrumentType.BRUKER_TOF) &&
                                 !BrukerMethodExporter.IsBrukerMethodPath(templateName))
                        {
                            MessageDlg.Show(this, Resources.ExportMethodDlg_btnBrowseTemplate_Click_The_chosen_folder_does_not_appear_to_contain_a_Bruker_TOF_method_template___The_folder_is_expected_to_have_a__m_extension__and_contain_the_file_submethods_xml_);
                            return;
                        }
                        textTemplateFile.Text = templateName;
                    }
                }

                return;
            }

            using (var openFileDialog = new OpenFileDialog
                {
                    Title = Resources.ExportMethodDlg_btnBrowseTemplate_Click_Method_Template,
                    // Extension based on currently selected type
                    CheckPathExists = true
                })
            {
                if (!string.IsNullOrEmpty(templateName))
                {
                    try
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(templateName);
                        openFileDialog.FileName = Path.GetFileName(templateName);
                    }
                    catch (ArgumentException)
                    {
                    } // Invalid characters
                    catch (PathTooLongException)
                    {
                    }
                }

                var listFileTypes = new List<string>();
                if (Equals(InstrumentType, ExportInstrumentType.ABI_QTRAP))
                {
                    listFileTypes.Add(MethodFilter(ExportInstrumentType.EXT_AB_SCIEX));
                }
                else if (Equals(InstrumentType, ExportInstrumentType.THERMO_TSQ) ||
                         Equals(InstrumentType, ExportInstrumentType.THERMO_LTQ))
                {
                    listFileTypes.Add(MethodFilter(ExportInstrumentType.EXT_THERMO));
                }
                else if (Equals(InstrumentType, ExportInstrumentType.WATERS_XEVO) ||
                         Equals(InstrumentType, ExportInstrumentType.WATERS_QUATTRO_PREMIER))
                {
                    listFileTypes.Add(MethodFilter(ExportInstrumentType.EXT_WATERS));
                }
                openFileDialog.Filter = TextUtil.FileDialogFiltersAll(listFileTypes.ToArray());

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    textTemplateFile.Text = openFileDialog.FileName;
                }
            }
        }

        private string MethodFilter(string ext)
        {
            return TextUtil.FileDialogFilter(string.Format(Resources.ExportMethodDlg_btnBrowseTemplate_Click__0__Method, InstrumentType), ext);
        }

        private void textMaxTransitions_TextChanged(object sender, EventArgs e)
        {
            int maxTrans;
            if(!int.TryParse(textMaxTransitions.Text, out maxTrans) || maxTrans < 1)
            {
                labelMethodNum.Text = string.Empty;
                return;
            }

            CalcMethodCount();
        }

        private void comboOptimizing_SelectedIndexChanged(object sender, EventArgs e)
        {
            CalcMethodCount();
        }

        #region Functional Test Support

        public class TransitionListView : IFormView { }
        public class IsolationListView : IFormView { }
        public class MethodView : IFormView { }

        public IFormView ShowingFormView
        {
            get
            {
                switch (_fileType)
                {
                    case ExportFileType.List:
                        return new TransitionListView();
                    case ExportFileType.IsolationList:
                        return new IsolationListView();
                    default:
                        return new MethodView();
                }
            }
        }

        public void SetTemplateFile(string templateFile)
        {
            textTemplateFile.Text = templateFile;
        }

        public void SetInstrument(string instrument)
        {
            if(ExportInstrumentType.TRANSITION_LIST_TYPES.ToList().Find(inst => Equals(inst, instrument)) == default(string))
                return;

            comboInstrument.SelectedText = instrument;
        }

        public void SetMethodType(ExportMethodType type)
        {
            comboTargetType.SelectedItem = type == ExportMethodType.Standard
                                               ? Resources.ExportMethodDlg_SetMethodType_Standard
                                               : Resources.ExportMethodDlg_SetMethodType_Scheduled;
        }

        public bool IsTargetTypeEnabled
        {
            get { return comboTargetType.Enabled; }
        }

        public bool IsOptimizeTypeEnabled
        {
            get {return comboOptimizing.Enabled;}
        }

        public string GetMaxLabelText
        {
           get { return labelMaxTransitions.Text; }
        }

        public bool IsMaxTransitionsEnabled
        {
           get { return textMaxTransitions.Enabled; }
        }

        public string GetDwellTimeLabel
        {
            get { return labelDwellTime.Text; }
        }

        public bool IsDwellTimeVisible
        {
            get { return textDwellTime.Visible; }
        }

        public bool IsRunLengthVisible
        {
            get{ return textRunLength.Visible; }
        }

        public bool IsPrimaryCountVisible
        {
            get { return textPrimaryCount.Visible; }
        }

        public int CalculationTime
        {
            get { return _exportProperties.MultiplexIsolationListCalculationTime; }
            set { _exportProperties.MultiplexIsolationListCalculationTime = value; }
        }

        public bool DebugCycles
        {
            get { return _exportProperties.DebugCycles; }
            set { _exportProperties.DebugCycles = value; }
        }

        #endregion
    }

    public class ExportDlgProperties : ExportProperties
    {
        private readonly ExportMethodDlg _dialog;

        public ExportDlgProperties(ExportMethodDlg dialog)
        {
            _dialog = dialog;
        }

        public bool ShowMessages { get; set; }

        public override void PerformLongExport(Action<IProgressMonitor> performExport)
        {
            if (!ShowMessages)
            {
                performExport(new SilentProgressMonitor());
                return;
            }

            using (var longWait = new LongWaitDlg
                    {
                        Text = Resources.ExportDlgProperties_PerformLongExport_Exporting_Methods
                    })
            {
                try
                {
                    var status = longWait.PerformWork(_dialog, 800, performExport);
                    if (status.IsError)
                        MessageDlg.Show(_dialog, status.ErrorException.Message);
                }
                catch (Exception x)
                {
                    MessageDlg.Show(_dialog, TextUtil.LineSeparate(Resources.ExportDlgProperties_PerformLongExport_An_error_occurred_attempting_to_export,
                                                                   x.Message));
                }
            }
        }

        private class SilentProgressMonitor : IProgressMonitor
        {
            public bool IsCanceled { get { return false; } }
            public void UpdateProgress(ProgressStatus status) { }
            public bool HasUI { get { return false; } }
        }
    }
}
