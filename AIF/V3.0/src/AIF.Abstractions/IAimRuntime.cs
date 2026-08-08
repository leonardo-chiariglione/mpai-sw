namespace AIF.Abstractions;
public interface IAimRuntime { Task StartAsync(); Task PauseAsync(); Task ResumeAsync(); Task StopAsync(); }
