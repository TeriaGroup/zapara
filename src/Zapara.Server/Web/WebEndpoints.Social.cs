namespace Zapara.Server.Web;

internal static partial class WebEndpoints
{
    private static void MapSocial(RouteGroupBuilder root)
    {
        var group = root.MapGroup("/social");
        Route(group, "GET", "/home", SocialHttp.Home);
        Route(group, "POST", "/invites", SocialHttp.Invite);
        Route(group, "POST", "/invites/{friendshipId}/accept", SocialHttp.Accept);
        Route(group, "POST", "/invites/{friendshipId}/decline", SocialHttp.Decline);
        Route(group, "GET", "/conversations/{conversationId}/messages", SocialHttp.Messages);
        Route(group, "POST", "/conversations/{conversationId}/messages", SocialHttp.Text);
        Route(group, "POST", "/conversations/{conversationId}/stickers", SocialHttp.Sticker);
        Route(group, "POST", "/conversations/{conversationId}/cards", SocialHttp.Card);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/edit", SocialHttp.Edit);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/delete", SocialHttp.Delete);
        Route(group, "POST", "/conversations/{conversationId}/messages/{messageId}/reaction", SocialHttp.React);
        Route(group, "POST", "/conversations/{conversationId}/images", SocialHttp.Image);
        Route(group, "POST", "/conversations/{conversationId}/files", SocialHttp.Document);
        Route(group, "POST", "/conversations/{conversationId}/voice", SocialHttp.Voice);
        Route(group, "POST", "/conversations/{conversationId}/circles", SocialHttp.Circle);
        Route(group, "GET", "/attachments/{attachmentId}", SocialHttp.Attachment, bootstrap: true);
    }
}
