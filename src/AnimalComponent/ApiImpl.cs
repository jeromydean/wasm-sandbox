namespace ScriptWorld.wit.exports.example.animal;

public class ApiImpl : IApi
{
  public static IApi.Animal GetAnimal()
  {
    return new IApi.Animal("Rex", "Dog", 3, true);
  }
}
