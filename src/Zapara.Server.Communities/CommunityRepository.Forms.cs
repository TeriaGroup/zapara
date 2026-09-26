using System.Text.Json;
using Zapara.Contracts.Communities;
namespace Zapara.Server.Communities;
internal sealed partial class CommunityRepository
{
    internal async Task<GroupFormListResponse> FormsAsync(Guid communityId,Guid topicId)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"read");
        if((await SpaceTopicAsync(communityId,topicId)).Kind!="forms") throw CommunityServiceException.InvalidRequest();
        var ids=new List<Guid>();
        await using(var command=Command($"SELECT form_id FROM {Msg}.group_forms WHERE community_id=@p0 AND topic_id=@p1 ORDER BY created_at DESC LIMIT 100",communityId,topicId))
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        var forms=new List<GroupFormResponse>();
        foreach(var id in ids) forms.Add(await FormAsync(communityId,id));
        return new(forms);
    }
    internal async Task<GroupFormListResponse> CreateFormAsync(Guid communityId,Guid topicId,GroupFormRequest request)
    {
        await RequireTopicPermissionAsync(communityId,topicId,"forms");
        GroupFormRules.Validate(request,Now);
        if(await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_forms WHERE topic_id=@p0",topicId)>=100) throw CommunityServiceException.InvalidRequest();
        await ExecuteAsync($"INSERT INTO {Msg}.group_forms(form_id,community_id,topic_id,title,description,deadline_at,anonymous,questions,created_by,created_at) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
            Guid.NewGuid(),communityId,topicId,request.Title.Trim(),request.Description,request.DeadlineAt,request.Anonymous,JsonSerializer.Serialize(request.Questions,CommunityJson.CreateOptions()),UserId,Now);
        return await FormsAsync(communityId,topicId);
    }
    private async Task<GroupFormResponse> FormAsync(Guid communityId,Guid formId)
    {
        await RequireMemberAsync(communityId);
        Guid topicId,creator; string title,description,questions; bool anonymous; DateTimeOffset? deadline; DateTimeOffset created;
        await using(var command=Command($"SELECT topic_id,title,description,deadline_at,anonymous,questions,created_by,created_at FROM {Msg}.group_forms WHERE community_id=@p0 AND form_id=@p1",communityId,formId))
        await using(var r=await command.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            topicId=r.GetGuid(0); title=r.GetString(1); description=r.GetString(2); deadline=r.IsDBNull(3)?null:AsUtc(r.GetFieldValue<DateTimeOffset>(3)); anonymous=r.GetBoolean(4); questions=r.GetString(5); creator=r.GetGuid(6); created=AsUtc(r.GetFieldValue<DateTimeOffset>(7));
        }
        await RequireTopicPermissionAsync(communityId,topicId,"read");
        var rights=await TopicPermissionsAsync(communityId,await SpaceTopicAsync(communityId,topicId));
        GroupFormAnswerResponse? own=null;
        await using(var command=Command($"SELECT answers,updated_at FROM {Msg}.group_form_answers WHERE form_id=@p0 AND user_id=@p1",formId,UserId))
        await using(var r=await command.ExecuteReaderAsync(ct))
            if(await r.ReadAsync(ct)) own=new(UserId,JsonSerializer.Deserialize<GroupFormAnswer[]>(r.GetString(0),CommunityJson.CreateOptions())!,AsUtc(r.GetFieldValue<DateTimeOffset>(1)));
        var count=creator==UserId?await ScalarAsync($"SELECT count(*)::int FROM {Msg}.group_form_answers WHERE form_id=@p0",formId):own is null?0:1;
        return new(formId,topicId,title,description,deadline,anonymous,JsonSerializer.Deserialize<GroupFormQuestion[]>(questions,CommunityJson.CreateOptions())!,creator,created,
            rights.Contains("formsRespond")&&(deadline is null||deadline>Now),creator==UserId,own,count);
    }
    internal async Task<GroupFormResponse> SubmitFormAsync(Guid communityId,Guid formId,GroupFormAnswerRequest request)
    {
        var form=await FormAsync(communityId,formId);
        if(!form.CanRespond) throw CommunityServiceException.Forbidden();
        if(request is null) throw CommunityServiceException.InvalidRequest();
        GroupFormRules.ValidateAnswers(form.Questions,request.Answers);
        await ExecuteAsync($"INSERT INTO {Msg}.group_form_answers(form_id,user_id,answers,updated_at) VALUES(@p0,@p1,@p2,@p3) ON CONFLICT(form_id,user_id) DO UPDATE SET answers=EXCLUDED.answers,updated_at=EXCLUDED.updated_at",
            formId,UserId,JsonSerializer.Serialize(request.Answers,CommunityJson.CreateOptions()),Now);
        return await FormAsync(communityId,formId);
    }
    internal async Task<GroupFormResponsesResponse> FormResponsesAsync(Guid communityId, Guid formId, Guid? after = null)
    {
        var form = await FormAsync(communityId, formId);
        if (!form.CanViewResponses) throw CommunityServiceException.Forbidden();
        var answers = new List<GroupFormAnswerResponse>();
        var cursors = new List<Guid>();
        await using var command = Command($"SELECT user_id,answers,updated_at,response_id FROM {Msg}.group_form_answers WHERE form_id=@p0 AND response_id>@p1 ORDER BY response_id LIMIT 51",formId,after??Guid.Empty);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            answers.Add(new(form.Anonymous ? null : reader.GetGuid(0), JsonSerializer.Deserialize<GroupFormAnswer[]>(reader.GetString(1), CommunityJson.CreateOptions())!, AsUtc(reader.GetFieldValue<DateTimeOffset>(2))));
            cursors.Add(reader.GetGuid(3));
        }
        var more = answers.Count > 50;
        if (more) answers.RemoveAt(50);
        return new(formId, answers, more ? cursors[49] : null, form.ResponseCount);
    }
}
