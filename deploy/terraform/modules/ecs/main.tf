# ── ALB ──────────────────────────────────────────────────────────────────────

resource "aws_security_group" "alb" {
  name   = "${var.name}-alb-${var.environment}"
  vpc_id = var.vpc_id
  ingress { from_port = 80;  to_port = 80;  protocol = "tcp"; cidr_blocks = ["0.0.0.0/0"] }
  ingress { from_port = 443; to_port = 443; protocol = "tcp"; cidr_blocks = ["0.0.0.0/0"] }
  egress  { from_port = 0;   to_port = 0;   protocol = "-1";  cidr_blocks = ["0.0.0.0/0"] }
}

resource "aws_lb" "main" {
  name               = "${var.name}-${var.environment}"
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = var.public_subnet_ids
}

resource "aws_lb_target_group" "api" {
  name        = "${var.name}-api-${var.environment}"
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"
  health_check {
    path                = "/health/live"
    healthy_threshold   = 2
    unhealthy_threshold = 3
    interval            = 30
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"
  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

# ── Security group for ECS tasks ─────────────────────────────────────────────

resource "aws_security_group" "ecs" {
  name   = "${var.name}-ecs-${var.environment}"
  vpc_id = var.vpc_id
  ingress {
    from_port       = 8080
    to_port         = 8080
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }
  egress { from_port = 0; to_port = 0; protocol = "-1"; cidr_blocks = ["0.0.0.0/0"] }
}

# ── ECS Cluster ───────────────────────────────────────────────────────────────

resource "aws_ecs_cluster" "main" {
  name = "${var.name}-${var.environment}"
  setting { name = "containerInsights"; value = "enabled" }
}

# ── IAM ───────────────────────────────────────────────────────────────────────

data "aws_iam_policy_document" "ecs_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals { type = "Service"; identifiers = ["ecs-tasks.amazonaws.com"] }
  }
}

resource "aws_iam_role" "execution" {
  name               = "${var.name}-ecs-execution-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role" "task" {
  name               = "${var.name}-ecs-task-${var.environment}"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume.json
}

resource "aws_iam_role_policy" "task_s3" {
  name = "s3-access"
  role = aws_iam_role.task.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
      Resource = "${var.s3_bucket_arn}/*"
    }]
  })
}

# ── CloudWatch log group ──────────────────────────────────────────────────────

resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/${var.name}-api-${var.environment}"
  retention_in_days = 30
}

resource "aws_cloudwatch_log_group" "migrator" {
  name              = "/ecs/${var.name}-migrator-${var.environment}"
  retention_in_days = 7
}

# ── Task definitions ──────────────────────────────────────────────────────────

resource "aws_ecs_task_definition" "migrator" {
  family                   = "${var.name}-migrator-${var.environment}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  container_definitions = jsonencode([{
    name      = "migrator"
    image     = var.migrator_image
    essential = true
    environment = [
      { name = "ConnectionStrings__DefaultConnection"; value = var.db_connection_string },
      { name = "ASPNETCORE_ENVIRONMENT"; value = "Production" }
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.migrator.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "migrator"
      }
    }
  }])
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name}-api-${var.environment}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.cpu
  memory                   = var.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  container_definitions = jsonencode([{
    name      = "api"
    image     = var.api_image
    essential = true
    portMappings = [{ containerPort = 8080; protocol = "tcp" }]
    environment = [
      { name = "ASPNETCORE_URLS";                      value = "http://+:8080" },
      { name = "ASPNETCORE_ENVIRONMENT";               value = "Production" },
      { name = "ConnectionStrings__DefaultConnection"; value = var.db_connection_string },
      { name = "Redis__ConnectionString";              value = "${var.redis_endpoint}:6379" },
      { name = "Jwt__Secret";                          value = var.jwt_secret },
      { name = "Storage__Endpoint";                    value = var.s3_bucket_name },
      { name = "Storage__UseSsl";                      value = "true" }
    ]
    healthCheck = {
      command     = ["CMD-SHELL", "curl -f http://localhost:8080/health/live || exit 1"]
      interval    = 30
      timeout     = 5
      retries     = 3
      startPeriod = 60
    }
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "api"
      }
    }
  }])
}

# ── ECS Service ───────────────────────────────────────────────────────────────

resource "aws_ecs_service" "api" {
  name            = "${var.name}-api-${var.environment}"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.ecs.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  depends_on = [aws_lb_listener.http]
}

# ── Outputs ───────────────────────────────────────────────────────────────────

output "alb_dns_name"           { value = aws_lb.main.dns_name }
output "cluster_name"           { value = aws_ecs_cluster.main.name }
output "migrator_task_definition" { value = aws_ecs_task_definition.migrator.arn }

# ── Variables ─────────────────────────────────────────────────────────────────

variable "name"                  {}
variable "environment"           {}
variable "aws_region"            {}
variable "vpc_id"                {}
variable "public_subnet_ids"     { type = list(string) }
variable "private_subnet_ids"    { type = list(string) }
variable "api_image"             {}
variable "migrator_image"        {}
variable "db_connection_string"  { sensitive = true }
variable "redis_endpoint"        {}
variable "jwt_secret"            { sensitive = true }
variable "s3_bucket_name"        {}
variable "s3_bucket_arn"         {}
variable "cpu"                   { default = 512 }
variable "memory"                { default = 1024 }
variable "desired_count"         { default = 1 }
