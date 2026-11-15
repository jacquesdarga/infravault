// Chaque test crée son propre dossier de données, mais SqliteConnection.ClearAllPools()
// agit sur tout le processus : exécuter les tests en parallèle les ferait s'arracher
// les connexions les uns aux autres.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
