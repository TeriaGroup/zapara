using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Chat;
using System.Security.Cryptography;
using Vograph.Core.Services.Communities;
using Vograph.Core.Services.Accounts;

namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    private ChatCapturedMedia? stagedRecording;
    public bool HasRecordedDraft=>stagedRecording is not null;
    public string RecordedDraftCaption=>stagedRecording is {} media?$"{(media.Kind=="voice"?"Голосовое сообщение":"Кружок")} · {media.DurationMs/1000} с · перед отправкой":"";
    private void StageRecording(ChatCapturedMedia media)
    {ClearRecordedDraft();stagedRecording=media;NotifyRecordedDraft();}
    private void ClearRecordedDraft()
    {StopPlayback();if(stagedRecording is {} media)CryptographicOperations.ZeroMemory(media.Bytes);stagedRecording=null;NotifyRecordedDraft();}
    private void NotifyRecordedDraft()
    {OnPropertyChanged(nameof(HasRecordedDraft));OnPropertyChanged(nameof(RecordedDraftCaption));OnPropertyChanged(nameof(CanAttachMedia));StartRecordingCommand.NotifyCanExecuteChanged();SendCommand.NotifyCanExecuteChanged();}
    [RelayCommand] private async Task PreviewRecording()
    {
        if(stagedRecording is not {} media)return;
        try{await player.PlayAsync(media.Kind,media.FileName,media.Bytes,CancellationToken.None);}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){Status="Не удалось воспроизвести запись.";}
    }
    [RelayCommand] private void DiscardRecording()=>ClearRecordedDraft();
    [RelayCommand] private async Task SendRecording()
    {
        if(stagedRecording is not {} media||!CanPostChannel||PreviewMode||IsBusy||conversationId is not Guid id||Api is null||Access is null)return;
        var ticket=navigationGeneration;
        using var operation=App.Work.Enter();Busy(true);
        try
        {
            var token=await Access(operation.Token);if(string.IsNullOrWhiteSpace(token)){ShowAccount();return;}
            if(!CurrentChat(id,ticket)||!operation.IsCurrent)return;
            var message=await GroupMedia.Place(Api,token,id,media.Kind,media.FileName,media.Bytes,replyTo,operation.Token,media.DurationMs,selectedGroupChannel?selectedTopicId:null);
            if(!CurrentChat(id,ticket)||!operation.IsCurrent)return;
            Messages.Add(Row(message));replyTo=null;HoldCaption="";ClearRecordedDraft();Status="";
        }
        catch(CommunityClientException){Status="Не удалось отправить запись. Она сохранена для повторной отправки.";}
        catch(AccountClientException ex){FailSession(ex);}
        catch(OperationCanceledException){}
        finally{if(operation.IsCurrent)Busy(false);}
    }
}
