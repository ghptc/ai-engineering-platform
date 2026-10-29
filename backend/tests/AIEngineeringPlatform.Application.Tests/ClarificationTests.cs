using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using NSubstitute;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public class ClarificationTests
{
    [Fact]
    public async Task OwnerAnswerIsPersistedAttachedAndResumedWithoutResettingLimits()
    {
        var store = Substitute.For<IPlatformStore>(); var publisher=Substitute.For<IExecutionPublisher>();
        var run = new ExecutionRun { Id="r",ProjectId="p",CurrentStage=RunStage.Failed,ResumeStage=RunStage.Implementing,FailureReason="Human clarification required: Which behavior?",Version=4,Cost=.5m,ToolCalls=3,Tasks=[new("Answer","Change answer",["Tests pass"])] };
        store.GetRunAsync("r",default).Returns(run);
        store.SaveRunAsync(Arg.Any<ExecutionRun>(),4,null,default).Returns(true);
        await new PlatformService(store,publisher).ClarifyAsync("r","Use the existing API",4,default);
        await store.Received().AppendAsync(Arg.Is<RecordEntry>(e=>e.Kind=="message"&&e.Name=="Client"&&e.Content=="Use the existing API"),default);
        await store.Received().AppendAsync(Arg.Is<RecordEntry>(e=>e.Kind=="instructionVersion"&&e.RunId=="r"),default);
        await store.Received().SaveRunAsync(Arg.Is<ExecutionRun>(r=>r.CurrentStage==RunStage.Implementing&&r.CurrentTask!.HumanInstructionIds!.Length==1&&r.Cost==.5m&&r.ToolCalls==3&&r.Policy==run.Policy&&r.RetryCount==1),4,null,default);
        Received.InOrder(()=>{store.AppendAsync(Arg.Is<RecordEntry>(e=>e.Kind=="message"),default);publisher.PublishAsync(Arg.Is<RecordEntry>(e=>e.Kind=="message"),default);});
        await Assert.ThrowsAsync<ConcurrencyException>(()=>new PlatformService(store,publisher).ClarifyAsync("r","Other",3,default));
    }
}
