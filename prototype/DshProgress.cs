namespace WhaleAlive;

// The bridge currently reports stages, not a step count or a completion percentage.
public sealed class DshProgress(Func<DateTimeOffset>? clock=null)
{
    readonly Func<DateTimeOffset> now=clock??(()=>DateTimeOffset.UtcNow);
    DateTimeOffset? received;
    string state="idle",lastOutcome="",agentName="DSH";
    public void Receive(string value,string name="DSH")
    {
        if(agentName!=name)lastOutcome="";
        agentName=name;
        state=value;received=now();
        if(value is "think" or "working" or "wait")lastOutcome="";
        else if(value is "celebrate" or "error")lastOutcome=value;
    }
    public string Text
    {
        get
        {
            if(received==null)return "还没连接到 AI 助手\n连接后点我查看任务进度。";
            if(now()-received.Value>TimeSpan.FromSeconds(8))return $"{agentName} 连接暂时中断\n暂时无法获取最新任务进度。";
            return state switch
            {
                "think"=>$"{agentName} · 正在思考\n正在分析任务、规划下一步。",
                "working"=>$"{agentName} · 正在执行\n正在调用工具处理任务。",
                "wait"=>$"{agentName} · 等待你确认\n回到 {agentName} 查看需要处理的事项。",
                "celebrate"=>$"{agentName} · 本轮任务已完成",
                "error"=>$"{agentName} · 执行遇到问题\n回到 {agentName} 查看具体原因。",
                _=>lastOutcome=="celebrate"?$"{agentName} · 最近一轮已完成\n当前没有正在执行的任务。":lastOutcome=="error"?$"{agentName} · 当前空闲\n最近一次执行出现异常。":$"{agentName} · 当前空闲\n没有正在执行的任务。"
            };
        }
    }
}
