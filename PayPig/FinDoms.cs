using Dalamud.Game.ClientState.Objects.SubKinds;
using Newtonsoft.Json;

namespace PayPig;

public class FinDoms {
    [JsonIgnore]
    public string Name
    {
        get => $"{Firstname} {Lastname}";
        set {
            var name = value.Split(' ');
            var fName = name[0].ToCharArray();
            var lName = name[1].ToCharArray();

            fName[0] = char.ToUpper(fName[0]);
            lName[0] = char.ToUpper(lName[0]);

            Firstname = new string(fName);
            Lastname = new string(lName);
        }
    }

    public string Firstname { get; set; }
    public string Lastname { get; set; }
    public string HomeworldName { get; set; }

    public FinDoms()
    {
        Firstname = string.Empty;
        Lastname = string.Empty;
        HomeworldName = string.Empty;
    }

    public FinDoms(PlayerCharacter actor)
    {
        Firstname = string.Empty;
        Lastname = string.Empty;

        Name = actor.Name.TextValue;
        HomeworldName = actor.HomeWorld.GameData!.Name;
    }

    [JsonConstructor]
    public FinDoms(string firstname, string lastname, string homeworldName)
    {
        Firstname = firstname;
        Lastname = lastname;
        HomeworldName = homeworldName;
    }
}