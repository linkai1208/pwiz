﻿/*
 * Original author: Nicholas Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2020 University of Washington - Seattle, WA
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
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using pwiz.Common.DataBinding;
using pwiz.Common.DataBinding.Controls;
using pwiz.Skyline.Model.Databinding.Entities;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.DocSettings.MetadataExtraction;
using pwiz.Skyline.Model.Results;
using pwiz.Skyline.Properties;
using pwiz.Skyline.SettingsUI;
using pwiz.Skyline.Util;
using pwiz.SkylineTestUtil;
// ReSharper disable LocalizableElement

namespace pwiz.SkylineTestFunctional
{
    [TestClass]
    public class MetadataRuleTest : AbstractFunctionalTest
    {
        [TestMethod]
        public void TestMetadataRules()
        {
            TestFilesZip = @"TestFunctional\MetadataRuleTest.zip";
            RunFunctionalTest();
        }

        protected override void DoTest()
        {
            RunUI(()=>SkylineWindow.OpenFile(TestFilesDir.GetTestPath("Rat_plasma.sky")));
            var documentSettingsDlg = ShowDialog<DocumentSettingsDlg>(SkylineWindow.ShowDocumentSettingsDialog);
            RunUI(()=>
            {
                documentSettingsDlg.SelectTab(DocumentSettingsDlg.TABS.metadata_rules);
            });
            var metadataRuleEditor = ShowDialog<MetadataRuleEditor>(documentSettingsDlg.AddMetadataRule);
            RunUI(() =>
            {
                metadataRuleEditor.RuleName = "SubjectId";
            });
            var metadataRuleStepEditor = ShowDialog<MetadataRuleStepEditor>(() => metadataRuleEditor.EditRule(0));
            RunUI(() =>
            {
                metadataRuleStepEditor.MetadataRuleStep = new MetadataRuleStep()
                    .ChangeSource(PropertyPath.Root.Property(nameof(ResultFile.FileName)))
                    .ChangePattern("[DH]_[0-9]+")
                    .ChangeTarget(PropertyPathForAnnotation("SubjectId"));
            });
            OkDialog(metadataRuleStepEditor, metadataRuleStepEditor.OkDialog);
            OkDialog(metadataRuleEditor, metadataRuleEditor.OkDialog);
            OkDialog(documentSettingsDlg, documentSettingsDlg.OkDialog);

            ImportResultsFiles(new[]
            {
                new MsDataFilePath(TestFilesDir.GetTestPath("D_102_REP1.mzML")),
                new MsDataFilePath(TestFilesDir.GetTestPath("H_146_Rep1.mzML"))
            });
            Assert.AreEqual(2, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms.Count);
            CollectionAssert.AreEqual(new[]{"D_102", "H_146"}, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms
                .Select(chrom=>chrom.Annotations.GetAnnotation("SubjectId")).ToList());
            documentSettingsDlg = ShowDialog<DocumentSettingsDlg>(SkylineWindow.ShowDocumentSettingsDialog);
            RunUI(() =>
            {
                documentSettingsDlg.SelectTab(DocumentSettingsDlg.TABS.metadata_rules);
            });
            metadataRuleEditor = ShowDialog<MetadataRuleEditor>(documentSettingsDlg.AddMetadataRule);
            RunUI(()=> metadataRuleEditor.RuleName = "BioReplicate");
            metadataRuleStepEditor = ShowDialog<MetadataRuleStepEditor>(() => metadataRuleEditor.EditRule(0));
            RunUI(() =>
            {
                metadataRuleStepEditor.MetadataRuleStep = new MetadataRuleStep()
                    .ChangeSource(PropertyPath.Root.Property(nameof(ResultFile.FileName)))
                    .ChangePattern("_([0-9]+)")
                    .ChangeReplacement("$1")
                    .ChangeTarget(PropertyPathForAnnotation("BioReplicate"));
            });
            WaitForConditionUI(() => ((BindingListSource) metadataRuleStepEditor.PreviewGrid.DataSource).IsComplete);
            Assert.AreEqual(2, metadataRuleStepEditor.PreviewGrid.RowCount);
            OkDialog(metadataRuleStepEditor, metadataRuleStepEditor.OkDialog);
            OkDialog(metadataRuleEditor, metadataRuleEditor.OkDialog);
            var metadataRuleListEditor = ShowDialog<EditListDlg<SettingsListBase<MetadataRule>, MetadataRule>>
                (documentSettingsDlg.EditMetadataRuleList);
            RunUI(()=>metadataRuleListEditor.SelectItem("SubjectId"));
            metadataRuleEditor = ShowDialog<MetadataRuleEditor>(metadataRuleListEditor.EditItem);
            RunUI(() =>
            {
                var grid = metadataRuleEditor.DataGridViewSteps;
                grid.CurrentCell = grid.Rows[0].Cells[metadataRuleEditor.ColumnPattern.Index];
                SetCurrentCellValue(grid, "([DH])_([0-9]+)");
            });
            WaitForConditionUI(() => ((BindingListSource) metadataRuleEditor.PreviewGrid.DataSource).IsComplete);
            RunUI(() =>
            {
                var grid = metadataRuleEditor.PreviewGrid;
                var colSubjectId = grid.Columns.OfType<DataGridViewColumn>()
                    .FirstOrDefault(col => col.HeaderText == "SubjectId");
                Assert.IsNotNull(colSubjectId);
                Assert.AreEqual("D_102", grid.Rows[0].Cells[colSubjectId.Index].Value);
            });
            RunUI(() =>
            {
                var grid = metadataRuleEditor.DataGridViewSteps;
                grid.CurrentCell = grid.Rows[0].Cells[metadataRuleEditor.ColumnReplacement.Index];
                SetCurrentCellValue(grid, "$1$2");
                grid.CurrentCell = grid.Rows[0].Cells[metadataRuleEditor.ColumnPattern.Index];
            });
            WaitForConditionUI(() => ((BindingListSource)metadataRuleEditor.PreviewGrid.DataSource).IsComplete);
            RunUI(() =>
            {
                var grid = metadataRuleEditor.PreviewGrid;
                var colSubjectId = grid.Columns.OfType<DataGridViewColumn>()
                    .FirstOrDefault(col => col.HeaderText == "SubjectId");
                Assert.IsNotNull(colSubjectId);
                Assert.AreEqual("D102", grid.Rows[0].Cells[colSubjectId.Index].Value);
            });
            OkDialog(metadataRuleEditor, metadataRuleEditor.OkDialog);
            OkDialog(metadataRuleListEditor, metadataRuleListEditor.OkDialog);
            OkDialog(documentSettingsDlg, documentSettingsDlg.OkDialog);
            CollectionAssert.AreEqual(new[] { "D102", "H146" }, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms
                .Select(chrom => chrom.Annotations.GetAnnotation("SubjectId")).ToList());
            var annotationDefBioReplicate =
                SkylineWindow.Document.Settings.DataSettings.AnnotationDefs.FirstOrDefault(def =>
                    def.Name == "BioReplicate");
            Assert.IsNotNull(annotationDefBioReplicate);
            CollectionAssert.AreEqual(new[] { 102.0, 146.0 }, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms
                .Select(chrom => chrom.Annotations.GetAnnotation(annotationDefBioReplicate)).ToList());

            documentSettingsDlg = ShowDialog<DocumentSettingsDlg>(SkylineWindow.ShowDocumentSettingsDialog);
            RunUI(() =>
            {
                documentSettingsDlg.SelectTab(DocumentSettingsDlg.TABS.metadata_rules);
            });
            metadataRuleListEditor = ShowDialog<EditListDlg<SettingsListBase<MetadataRule>, MetadataRule>>
                (documentSettingsDlg.EditMetadataRuleList);
            RunUI(() =>
            {
                metadataRuleListEditor.SelectItem("SubjectId");
            });
            metadataRuleEditor = ShowDialog<MetadataRuleEditor>(metadataRuleListEditor.EditItem);
            RunUI(() =>
            {
                var grid = metadataRuleEditor.DataGridViewSteps;
                var newRow = grid.Rows[grid.RowCount - 1];
                Assert.IsTrue(newRow.IsNewRow);
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnPattern.Index];
                SetCurrentCellValue(grid, "D_");
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnReplacement.Index];
                SetCurrentCellValue(grid, "Diseased");
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnTarget.Index];
                SetCurrentCellValue(grid, "Condition");
                newRow = grid.Rows[grid.RowCount - 1];
                Assert.IsTrue(newRow.IsNewRow);
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnPattern.Index];
                SetCurrentCellValue(grid, "H_");
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnReplacement.Index];
                SetCurrentCellValue(grid, "Healthy");
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnTarget.Index];
                SetCurrentCellValue(grid, "Condition");
                grid.CurrentCell = newRow.Cells[metadataRuleEditor.ColumnSource.Index];
            });
            OkDialog(metadataRuleEditor, metadataRuleEditor.OkDialog);
            OkDialog(metadataRuleListEditor, metadataRuleListEditor.OkDialog);
            OkDialog(documentSettingsDlg, documentSettingsDlg.OkDialog);
            CollectionAssert.AreEqual(new[] { "Diseased", "Healthy" }, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms
                .Select(chrom => chrom.Annotations.GetAnnotation("Condition")).ToList());
            ImportResultsFiles(new[]
            {
                new MsDataFilePath(TestFilesDir.GetTestPath("D_102_REP2.mzML")),
                new MsDataFilePath(TestFilesDir.GetTestPath("H_146_Rep2.mzML"))
            });
            CollectionAssert.AreEqual(new[] { "Diseased", "Healthy", "Diseased", "Healthy" }, SkylineWindow.Document.Settings.MeasuredResults.Chromatograms
                .Select(chrom => chrom.Annotations.GetAnnotation("Condition")).ToList());
        }

        private void SetCurrentCellValue(DataGridView grid, object value)
        {
            IDataGridViewEditingControl editingControl = null;
            DataGridViewEditingControlShowingEventHandler onEditingControlShowing =
                (sender, args) =>
                {
                    Assume.IsNull(editingControl);
                    editingControl = args.Control as IDataGridViewEditingControl;
                };
            try
            {
                grid.EditingControlShowing += onEditingControlShowing;
                grid.BeginEdit(true);
                if (null != editingControl)
                {
                    editingControl.EditingControlFormattedValue = value;
                }
                else
                {
                    grid.CurrentCell.Value = value;
                }
            }
            finally
            {
                grid.EditingControlShowing -= onEditingControlShowing;
            }
        }

        private PropertyPath PropertyPathForAnnotation(string annotationName)
        {
            return PropertyPath.Root.Property(nameof(ResultFile.Replicate))
                .Property(AnnotationDef.ANNOTATION_PREFIX + annotationName);
        }
    }
}
