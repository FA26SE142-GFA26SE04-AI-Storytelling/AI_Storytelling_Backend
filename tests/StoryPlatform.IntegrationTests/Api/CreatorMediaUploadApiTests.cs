using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

// Real routing/auth/form binding, with application service stubbed; provider/storage are not exercised here.
public sealed class CreatorMediaUploadApiTests
{
    public class RecordingMedia : DispatchProxy
    {
        public object?[]? LastArguments;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IMediaGenerationService.SelectCreatorMediaAsync)) throw new NotSupportedException();
            LastArguments=args;
            return Task.FromResult(new CreatorMediaCommandResult(100,10,1,"Queued","/progress","/package"));
        }
    }
    private sealed class Factory : ApiFactory
    {
        public IMediaGenerationService Media=DispatchProxy.Create<IMediaGenerationService,RecordingMedia>();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(s=>
            {
                s.RemoveAll(x=>x.ServiceType==typeof(IMediaGenerationService));
                s.AddSingleton(Media);
            });
        }
    }
    private static MultipartFormDataContent Form(bool includeRights=true)
    {
        var form=new MultipartFormDataContent();
        var file=new ByteArrayContent([137,80,78,71,13,10,26,10]);
        file.Headers.ContentType=new MediaTypeHeaderValue("image/png");
        form.Add(file,"file","picture.png");
        foreach(var field in new Dictionary<string,string> { ["operationId"]="http-upload",["storyVersionId"]="10",
            ["packageRevision"]="1",["expectedConfirmed"]="false",["sourceDeclaration"]="Own work" })
            form.Add(new StringContent(field.Value),field.Key);
        if(includeRights) form.Add(new StringContent("I own the rights"),"rightsDeclaration");
        return form;
    }
    [Fact]
    public async Task Creator_multipart_binds_file_and_commands_and_returns_202()
    {
        using var factory=new Factory(); using var client=await factory.ClientForNewUserAsync(UserRole.ContentCreator);
        using var form=Form();
        var response=await client.PostAsync("/api/v1/creator/stories/1/media/illustration-beats/2/upload",form);
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var args=((RecordingMedia)factory.Media).LastArguments!;
        Assert.Equal(1,args[1]); Assert.Equal(2,args[2]); Assert.Equal(true,args[3]);
        Assert.False(((CreatorMediaCommand)args[4]!).ExpectedConfirmed);
        Assert.Equal("image/png",((GeneratedMedia)args[6]!).MimeType);
    }
    [Fact]
    public async Task Parent_cannot_upload_and_missing_rights_is_400_before_service()
    {
        using var factory=new Factory(); using var parent=await factory.ClientForNewUserAsync(UserRole.User);
        using var parentForm=Form();
        Assert.Equal(HttpStatusCode.Forbidden,(await parent.PostAsync("/api/v1/creator/stories/1/media/segments/2/upload",parentForm)).StatusCode);
        using var creator=await factory.ClientForNewUserAsync(UserRole.ContentCreator);
        using var invalid=Form(false);
        Assert.Equal(HttpStatusCode.BadRequest,(await creator.PostAsync("/api/v1/creator/stories/1/media/illustration-beats/2/upload",invalid)).StatusCode);
        Assert.Null(((RecordingMedia)factory.Media).LastArguments);
    }
}
