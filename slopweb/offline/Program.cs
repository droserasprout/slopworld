switch (args)
{
    case ["--core", var root, var probe]:
        CoreBootstrap.Apply(root, probe);
        break;
    case ["--eco", var root, var mod]:
        EcoWorkspace.Apply(root, mod);
        break;
    default:
        Console.Error.WriteLine("Usage: Offline --core ROOT PROBE | --eco ROOT MOD");
        return 2;
}
return 0;
