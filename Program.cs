using Azure;
using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.Models;
using Azure.AI.FormRecognizer.Training;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace formrecognizer_quickstart
{
    class Program
    {
        static void Main(string[] args)
        {
            var t1 = RunFormRecognizerClient();

            Task.WaitAll(t1);       
        }
    static async Task RunFormRecognizerClient()
    { 
        string endpoint = Environment.GetEnvironmentVariable(
            "FORM_RECOGNIZER_ENDPOINT");
        string apiKey = Environment.GetEnvironmentVariable(
            "FORM_RECOGNIZER_KEY");
        var credential = new AzureKeyCredential(apiKey);
        
        var trainingClient = new FormTrainingClient(new Uri(endpoint), credential);
        var recognizerClient = new FormRecognizerClient(new Uri(endpoint), credential);
            string trainingDataUrl = "<SAS-URL-of-your-form-folder-in-blob-storage>";
        string formUrl = "<SAS-URL-of-a-form-in-blob-storage>";
        string receiptUrl = "https://docs.microsoft.com/azure/cognitive-services/form-recognizer/media"
        + "/contoso-allinone.jpg";

        // Call Form Recognizer scenarios:
        Console.WriteLine("Get form content...");
        await GetContent(recognizerClient, formUrl);

        Console.WriteLine("Analyze receipt...");
        await AnalyzeReceipt(recognizerClient, receiptUrl);

        Console.WriteLine("Train Model with training data...");
        string modelId = await TrainModel(trainingClient, trainingDataUrl);

        Console.WriteLine("Analyze PDF form...");
        await AnalyzePdfForm(recognizerClient, modelId, formUrl);

        Console.WriteLine("Manage models...");
        await ManageModels(trainingClient, trainingDataUrl) ;
    }

    private static async Task<IReadOnlyList<FormPage>> GetContent(
        FormRecognizerClient recognizerClient, string invoiceUri)
    {
        Response<FormPageCollection> formPages = await recognizerClient
            .StartRecognizeContentFromUri(new Uri(invoiceUri))
            .WaitForCompletionAsync();
                foreach (FormPage page in formPages.Value)
        {
            Console.WriteLine($"Form Page {page.PageNumber} has {page.Lines.Count}" + 
                $" lines.");
        
            for (int i = 0; i < page.Lines.Count; i++)
            {
                FormLine line = page.Lines[i];
                Console.WriteLine($"    Line {i} has {line.Words.Count}" + 
                    $" word{(line.Words.Count > 1 ? "s" : "")}," +
                    $" and text: '{line.Text}'.");
            }
        
            for (int i = 0; i < page.Tables.Count; i++)
            {
                FormTable table = page.Tables[i];
                Console.WriteLine($"Table {i} has {table.RowCount} rows and" +
                    $" {table.ColumnCount} columns.");
                foreach (FormTableCell cell in table.Cells)
                {
                    Console.WriteLine($"    Cell ({cell.RowIndex}, {cell.ColumnIndex})" +
                        $" contains text: '{cell.Text}'.");
                }
            }
        }
        return formPages.Value;
    }
    private static async Task<RecognizedFormCollection> AnalyzeReceipt(
        FormRecognizerClient recognizerClient, string receiptUri)
    {
        Response<RecognizedFormCollection> receipts = await recognizerClient.StartRecognizeReceiptsFromUri(new Uri(receiptUri))
        .WaitForCompletionAsync();

        foreach (var receipt in receipts.Value)
        {
            FormField merchantNameField;
            if (receipt.Fields.TryGetValue("MerchantName", out merchantNameField))
            {
                if (merchantNameField.Value.Type == FieldValueType.String)
                {
                    string merchantName = merchantNameField.Value.AsString();

                    Console.WriteLine($"Merchant Name: '{merchantName}', with confidence {merchantNameField.Confidence}");
                }
            }

            FormField transactionDateField;
            if (receipt.Fields.TryGetValue("TransactionDate", out transactionDateField))
            {
                if (transactionDateField.Value.Type == FieldValueType.Date)
                {
                    DateTime transactionDate = transactionDateField.Value.AsDate();

                    Console.WriteLine($"Transaction Date: '{transactionDate}', with confidence {transactionDateField.Confidence}");
                }
            }
            FormField itemsField;
            if (receipt.Fields.TryGetValue("Items", out itemsField))
            {
                if (itemsField.Value.Type == FieldValueType.List)
                {
                    foreach (FormField itemField in itemsField.Value.AsList())
                    {
                        Console.WriteLine("Item:");

                        if (itemField.Value.Type == FieldValueType.Dictionary)
                        {
                            IReadOnlyDictionary<string, FormField> itemFields = itemField.Value.AsDictionary();

                            FormField itemNameField;
                            if (itemFields.TryGetValue("Name", out itemNameField))
                            {
                                if (itemNameField.Value.Type == FieldValueType.String)
                                {
                                    string itemName = itemNameField.Value.AsString();

                                    Console.WriteLine($"    Name: '{itemName}', with confidence {itemNameField.Confidence}");
                                }
                            }

                            FormField itemTotalPriceField;
                            if (itemFields.TryGetValue("TotalPrice", out itemTotalPriceField))
                            {
                                if (itemTotalPriceField.Value.Type == FieldValueType.Float)
                                {
                                    float itemTotalPrice = itemTotalPriceField.Value.AsFloat();

                                    Console.WriteLine($"    Total Price: '{itemTotalPrice}', with confidence {itemTotalPriceField.Confidence}");
                                }
                            }
                        }
                    }
                }
            }
            FormField totalField;
            if (receipt.Fields.TryGetValue("Total", out totalField))
            {
                if (totalField.Value.Type == FieldValueType.Float)
                {
                    float total = totalField.Value.AsFloat();

                    Console.WriteLine($"Total: '{total}', with confidence '{totalField.Confidence}'");
                }
            }
        }

        return receipts.Value;
    }

    private static async Task<string> TrainModel(
        FormTrainingClient trainingClient, string trainingFileUrl)
    {
        CustomFormModel model = await trainingClient
            .StartTrainingAsync(new Uri(trainingFileUrl), useTrainingLabels: false).WaitForCompletionAsync();
        
        Console.WriteLine($"Custom Model Info:");
        Console.WriteLine($"    Model Id: {model.ModelId}");
        Console.WriteLine($"    Model Status: {model.Status}");
        Console.WriteLine($"    Requested on: {model.TrainingStartedOn}");
        Console.WriteLine($"    Completed on: {model.TrainingCompletedOn}");
        foreach (CustomFormSubmodel submodel in model.Submodels)
        {
            Console.WriteLine($"Submodel Form Type: {submodel.FormType}");
            foreach (CustomFormModelField field in submodel.Fields.Values)
            {
                Console.Write($"    FieldName: {field.Name}");
                if (field.Label != null)
                {
                    Console.Write($", FieldLabel: {field.Label}");
                }
                Console.WriteLine("");
            }
        }
        return model.ModelId;
    }

    private static async Task<string> TrainModelWithLabelsAsync(
        FormTrainingClient trainingClient, string trainingFileUrl)
    {
        CustomFormModel model = await trainingClient
        .StartTrainingAsync(new Uri(trainingFileUrl), useTrainingLabels: true).WaitForCompletionAsync();
        
        Console.WriteLine($"Custom Model Info:");
        Console.WriteLine($"    Model Id: {model.ModelId}");
        Console.WriteLine($"    Model Status: {model.Status}");
        Console.WriteLine($"    Requested on: {model.TrainingStartedOn}");
        Console.WriteLine($"    Completed on: {model.TrainingCompletedOn}");        
        foreach (CustomFormSubmodel submodel in model.Submodels)
        {
            Console.WriteLine($"Submodel Form Type: {submodel.FormType}");
            foreach (CustomFormModelField field in submodel.Fields.Values)
            {
                Console.Write($"    FieldName: {field.Name}");
                if (field.Accuracy != null)
                {
                    Console.Write($", Accuracy: {field.Accuracy}");
                }
                Console.WriteLine("");
            }
        }
        return model.ModelId;
        
    }

        // Analyze PDF form data
    private static async Task AnalyzePdfForm(
        FormRecognizerClient formClient, string modelId, string pdfFormFile)
    {    
        Response<RecognizedFormCollection> forms = await formClient
            .StartRecognizeCustomFormsFromUri(modelId.ToString(), new Uri(pdfFormFile))
            .WaitForCompletionAsync();
        foreach (RecognizedForm form in forms.Value)
        {
            Console.WriteLine($"Form of type: {form.FormType}");
            foreach (FormField field in form.Fields.Values)
            {
                Console.WriteLine($"Field '{field.Name}: ");
        
                if (field.LabelData != null)
                {
                    Console.WriteLine($"    Label: '{field.LabelData.Text}");
                }
        
                Console.WriteLine($"    Value: '{field.ValueData.Text}");
                Console.WriteLine($"    Confidence: '{field.Confidence}");
            }
        }
    }

    private static async Task ManageModels(
        FormTrainingClient trainingClient, string trainingFileUrl)
    {
        // Check number of models in the FormRecognizer account, 
        // and the maximum number of models that can be stored.
        AccountProperties accountProperties = trainingClient.GetAccountProperties();
        Console.WriteLine($"Account has {accountProperties.CustomModelCount} models.");
        Console.WriteLine($"It can have at most {accountProperties.CustomModelLimit}" +
            $" models.");
        // List the first ten or fewer models currently stored in the account.
        Pageable<CustomFormModelInfo> models = trainingClient.GetCustomModels();
        
        foreach (CustomFormModelInfo modelInfo in models.Take(10))
        {
            Console.WriteLine($"Custom Model Info:");
            Console.WriteLine($"    Model Id: {modelInfo.ModelId}");
            Console.WriteLine($"    Model Status: {modelInfo.Status}");
            Console.WriteLine($"    Created On: {modelInfo.TrainingStartedOn}");
            Console.WriteLine($"    Last Modified: {modelInfo.TrainingCompletedOn}");
        }

        // Create a new model to store in the account
        CustomFormModel model = await trainingClient.StartTrainingAsync(
            new Uri(trainingFileUrl), useTrainingLabels: false).WaitForCompletionAsync();
        
        // Get the model that was just created
        CustomFormModel modelCopy = trainingClient.GetCustomModel(model.ModelId);
        
        Console.WriteLine($"Custom Model {modelCopy.ModelId} recognizes the following" +
            " form types:");
        
        foreach (CustomFormSubmodel subModel in modelCopy.Submodels)
        {
            Console.WriteLine($"SubModel Form Type: {subModel.FormType}");
            foreach (CustomFormModelField field in subModel.Fields.Values)
            {
                Console.Write($"    FieldName: {field.Name}");
                if (field.Label != null)
                {
                    Console.Write($", FieldLabel: {field.Label}");
                }
                Console.WriteLine("");
            }
        }

            // Delete the model from the account.
        trainingClient.DeleteModel(model.ModelId);
    }
    }    

}
