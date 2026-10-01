using Hajimi.Behavior;

namespace Hajimi.Audio;

public sealed record AudioCue(string Key,bool Loop=false,double RepeatSeconds=0);
public sealed class AudioPolicy(int seed=0)
{
    private readonly Random random=seed==0?new Random():new Random(seed);
    private int lastHiss=-1;
    public AudioCue? Select(PetState state,bool snore)=>state switch
    {
        PetState.Sleep=>snore?new("snore",true):null,
        PetState.Chew or PetState.PostHissChew=>new("growl",true),
        PetState.Hiss or PetState.Pounce=>new(ChooseHiss()),
        PetState.SitHiss=>new("hiss-1"),
        PetState.Scared=>new("scared"),
        PetState.HungryWait=>new("meow",false,6),
        PetState.Sated=>new("satisfied"),
        _=>null
    };
    private string ChooseHiss()
    {
        int chosen;
        do { chosen=random.Next(3); } while(chosen==lastHiss);
        lastHiss=chosen; return $"hiss-{chosen+1}";
    }
}
