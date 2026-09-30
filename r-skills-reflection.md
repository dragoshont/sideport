# My Reflection About Learning R

## R trip-up

For me, the main trip-up in R was when I joined two datasets. I used `left_join()` and the code was running, but the result was not what I expected. I had missing values and also some duplicated rows because the key columns did not have exactly the same type and some identifiers were not unique.

This was frustrating because R did not give me a clear error. The command worked, so at first I believed that the result was correct. After I checked the data more carefully, I understood that code can run without error but still give a wrong result for the analysis.

From this experience, I learned to check the column names, data types, duplicate identifiers, and number of rows before and after a join. I also try now to inspect the result after every important step instead of running the complete script and checking only the final output.

## My confidence with R now

My confidence with R is better than when I started, but I still do not feel completely independent. I can start from a blank script when the task is familiar. For example, I can load the packages, import a dataset, inspect the variables, clean the data, calculate summaries, and make a basic visualisation.

When the task is new or more complicated, I normally prefer to copy and adapt an example that I trust. I do not see this as only copying, because I try to understand what every line is doing and change it for my own data. I also run the code in smaller parts and check the intermediate results.

I still go to the documentation for joins, reshaping data, working with dates, more advanced `ggplot2` options, and statistical models. Model arguments and assumptions are especially something that I do not want to guess. My next objective is to become more comfortable with building an unfamiliar analysis from smaller steps, without needing a complete example from the beginning.

## Real-world context

My main reason for improving my R skills is to prepare for a future role where I will work with data. R can help me import, clean, analyse, visualise, and report data in one reproducible process. This is important because real datasets are not always clean and organised like the datasets used in exercises.

In a future data role, I will need to do more than produce a graph or a statistical result. I will also need to check if the data is correct, explain the decisions that I made, identify possible problems, and communicate the results to people with different technical knowledge.

For this reason, I want to improve especially in joining datasets, interpreting statistical models, and debugging scripts. These skills can help me move from following examples to completing reliable analyses by myself. I think that knowing how to check and explain the work is as important as knowing the R syntax.

## Peer reflection

I did not read a peer's post for this activity, so I cannot honestly say that I learned a specific idea from another student's reflection. My answers are based on my own experience and on the course materials.

For a future activity, I would like to read at least one peer's post. It could be useful to compare how another person approaches the same problem. Maybe they use a better way to organise the script, check the data, or solve an error. This comparison could help me see habits that I need to improve in my own work.

## Skills matrix

For this self-assessment, I use the following levels:

- **D — Developing:** I can do the task with an example or some guidance, but I am not yet consistent.
- **C — Competent:** I can complete a familiar task by myself, but sometimes I still check the documentation.
- **S — Skilled:** I can work with unfamiliar situations, solve problems, and explain the process to another person.

| Skill | Level | My next action |
|---|:---:|---|
| Starting and organising an R script or project | **C** | I will start one small analysis from a blank script each week. |
| Importing common data files | **C** | I will practise importing an unfamiliar dataset and check that the variables loaded correctly. |
| Inspecting data types, missing values, and duplicates | **C** | I will use the same inspection checklist before I start every analysis. |
| Cleaning and transforming variables | **C** | I will repeat a familiar cleaning process without copying my previous complete script. |
| Reshaping and joining datasets | **D** | I will practise joins and pivots and compare the keys and row numbers before and after each operation. |
| Grouping data and calculating descriptive summaries | **C** | I will make sure that I can explain every summary in simple language. |
| Producing and improving visualisations | **C** | I will create a graph from a research question instead of starting with a finished example. |
| Running and interpreting statistical models | **D** | I will review the assumptions and practise explaining coefficients and uncertainty. |
| Debugging, documenting, and reproducing an analysis | **D** | I will rerun scripts in a fresh session and separate errors into smaller examples. |

## Overall reflection

Overall, I consider myself competent with routine R tasks and still developing in more complex areas. I can work independently when I recognise the type of task, but I need examples and documentation when the situation is unfamiliar.

My priority is not to stop using documentation, because I think that documentation is a normal part of programming. Instead, I want to become better at knowing what information I need, testing possible solutions, and checking that the result is correct. With more practice, I want to move gradually from adapting complete examples to planning and completing an analysis by myself.
